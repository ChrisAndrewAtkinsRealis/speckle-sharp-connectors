using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Receive;

/// <summary>
/// Bakes a received model version into the active DGN model. Atomic objects are converted and added to the
/// model; cell instances/definitions are reconstructed as shared cells via the instance baker.
/// </summary>
/// <remarks>
/// MVP: objects are baked onto the active level. Per-collection level creation, material/color proxies and
/// pre-receive cleanup of previously baked elements are follow-ups.
/// </remarks>
public class MicroStationHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly IReceiveConversionHandler _conversionHandler;
  private readonly MicroStationInstanceBaker _instanceBaker;
  private readonly MicroStationLevelBaker _levelBaker;
  private readonly MicroStationItemTypeBaker _itemTypeBaker;
  private readonly MicroStationColorBaker _colorBaker;
  private readonly ICivilHostRebuilder _civilRebuilder;

  public MicroStationHostObjectBuilder(
    IRootToHostConverter converter,
    RootObjectUnpacker rootObjectUnpacker,
    IReceiveConversionHandler conversionHandler,
    MicroStationInstanceBaker instanceBaker,
    MicroStationLevelBaker levelBaker,
    MicroStationItemTypeBaker itemTypeBaker,
    MicroStationColorBaker colorBaker,
    ICivilHostRebuilder civilRebuilder
  )
  {
    _converter = converter;
    _rootObjectUnpacker = rootObjectUnpacker;
    _conversionHandler = conversionHandler;
    _instanceBaker = instanceBaker;
    _levelBaker = levelBaker;
    _itemTypeBaker = itemTypeBaker;
    _colorBaker = colorBaker;
    _civilRebuilder = civilRebuilder;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    onOperationProgressed.Report(new("Converting", null));

    string baseLayerName = $"SPK-{projectName}-{modelName}";

    // 1 - unpack root and split atomic objects from instance components (cells)
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    var results = new HashSet<ReceiveConversionResult>();
    var bakedObjectIds = new HashSet<string>();
    var applicationIdMap = new Dictionary<string, List<BDE.Element>>();
    int count = 0;

    // pre-create all levels once (durable ids before elements reference them)
    _levelBaker.EnsureLevels(atomicObjects.Select(GetLevelName).Distinct());

    // index received colours so they can be applied to baked elements
    _colorBaker.ParseColors(unpackedRoot.ColorProxies);

    // 2 - convert atomic objects (definition children + regular geometry), keeping an app-id -> element map
    foreach (var traversalContext in atomicObjects)
    {
      Base atomicObject = traversalContext.Current;
      onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));

      // civil entities (Alignment/Corridor) are regenerated natively via the CifNET edit API, not baked as geometry
      if (_civilRebuilder.CanRebuild(atomicObject))
      {
        var civilEx = _conversionHandler.TryConvert(() =>
        {
          cancellationToken.ThrowIfCancellationRequested();
          foreach (string bakedId in _civilRebuilder.Rebuild(atomicObject))
          {
            bakedObjectIds.Add(bakedId);
            results.Add(new(Status.SUCCESS, atomicObject, bakedId, "Civil"));
          }
        });
        if (civilEx is not null)
        {
          results.Add(new(Status.ERROR, atomicObject, null, null, civilEx));
        }
        continue;
      }

      string levelName = GetLevelName(traversalContext);

      var ex = _conversionHandler.TryConvert(() =>
      {
        cancellationToken.ThrowIfCancellationRequested();

        string objectId = atomicObject.applicationId ?? atomicObject.id.NotNull();
        var convertedElements = ConvertAndBake(atomicObject, levelName, objectId);
        applicationIdMap[objectId] = convertedElements;

        foreach (var element in convertedElements)
        {
          string elementId = element.ElementId.ToString();
          bakedObjectIds.Add(elementId);
          results.Add(new(Status.SUCCESS, atomicObject, elementId, element.GetType().ToString()));
        }
      });
      if (ex is not null)
      {
        results.Add(new(Status.ERROR, atomicObject, null, null, ex));
      }
    }

    // 3 - bake cell instances + definitions
    var instanceComponentsWithPath = instanceComponents
      .Select(tc => (Array.Empty<Collection>(), (IInstanceComponent)tc.Current))
      .ToList();

    if (unpackedRoot.DefinitionProxies is { Count: > 0 })
    {
      instanceComponentsWithPath.AddRange(
        unpackedRoot.DefinitionProxies.Select(proxy => (Array.Empty<Collection>(), (IInstanceComponent)proxy))
      );
    }

    if (instanceComponentsWithPath.Count > 0)
    {
      var bakeResult = _instanceBaker.BakeInstances(
        instanceComponentsWithPath,
        applicationIdMap,
        baseLayerName,
        onOperationProgressed
      );

      bakedObjectIds.RemoveWhere(id => bakeResult.ConsumedObjectIds.Contains(id));
      bakedObjectIds.UnionWith(bakeResult.CreatedInstanceIds);
      results.RemoveWhere(r => r.ResultId is not null && bakeResult.ConsumedObjectIds.Contains(r.ResultId));
      results.UnionWith(bakeResult.InstanceConversionResults);
    }

    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, results));
  }

  private List<BDE.Element> ConvertAndBake(Base atomicObject, string levelName, string objectId)
  {
    var baked = new List<BDE.Element>();
    object converted = _converter.Convert(atomicObject);

    switch (converted)
    {
      case BDE.Element element:
        AddToModel(element, levelName, objectId, atomicObject, baked);
        break;

      // data object conversions return element/base pairs
      case IEnumerable<(BDE.Element, Base)> typedList:
        foreach (var (element, _) in typedList)
        {
          AddToModel(element, levelName, objectId, atomicObject, baked);
        }
        break;

      // display value fallback conversions are zipped as (object, Base) pairs by ConverterWithFallback
      case IEnumerable<(object, Base)> fallbackList:
        foreach (var (obj, _) in fallbackList)
        {
          if (obj is BDE.Element element)
          {
            AddToModel(element, levelName, objectId, atomicObject, baked);
          }
        }
        break;

      case IEnumerable<BDE.Element> elements:
        foreach (var element in elements)
        {
          AddToModel(element, levelName, objectId, atomicObject, baked);
        }
        break;

      default:
        throw new SpeckleConversionException(
          $"Conversion of {atomicObject.speckle_type} returned an unexpected result: {converted?.GetType().Name}"
        );
    }

    return baked;
  }

  private void AddToModel(BDE.Element element, string levelName, string objectId, Base source, List<BDE.Element> baked)
  {
    // assign the received level (recreating the source structure), Item Types, and colour before persisting the element
    _levelBaker.SetElementLevel(element, levelName);
    _itemTypeBaker.ApplyItemTypes(element, source!);
    _colorBaker.ApplyColor(element, objectId!);

    var status = element.AddToModel();
    if (status == BDPN.StatusInt.Error)
    {
      throw new SpeckleConversionException("Failed to add element to the model.");
    }

    baked.Add(element);
  }

  /// <summary>
  /// The level for a received object is the name of its nearest ancestor collection in the traversal path
  /// (levels are modelled as collections on send). Reference sub-collections resolve to their inner level.
  /// </summary>
  private static string GetLevelName(TraversalContext traversalContext)
  {
    var context = traversalContext.Parent;
    while (context is not null)
    {
      if (context.Current is Collection collection && !string.IsNullOrEmpty(collection.name))
      {
        return collection.name;
      }

      context = context.Parent;
    }

    return "Default";
  }
}

/// <summary>
/// Exception thrown when baking a converted object into the DGN model fails.
/// </summary>
public class SpeckleConversionException : Exception
{
  public SpeckleConversionException() { }

  public SpeckleConversionException(string message)
    : base(message) { }

  public SpeckleConversionException(string message, Exception innerException)
    : base(message, innerException) { }
}
