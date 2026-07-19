using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
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

  public MicroStationHostObjectBuilder(
    IRootToHostConverter converter,
    RootObjectUnpacker rootObjectUnpacker,
    IReceiveConversionHandler conversionHandler,
    MicroStationInstanceBaker instanceBaker
  )
  {
    _converter = converter;
    _rootObjectUnpacker = rootObjectUnpacker;
    _conversionHandler = conversionHandler;
    _instanceBaker = instanceBaker;
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

    // 2 - convert atomic objects (definition children + regular geometry), keeping an app-id -> element map
    foreach (var traversalContext in atomicObjects)
    {
      Base atomicObject = traversalContext.Current;
      onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));

      var ex = _conversionHandler.TryConvert(() =>
      {
        cancellationToken.ThrowIfCancellationRequested();

        string objectId = atomicObject.applicationId ?? atomicObject.id.NotNull();
        var convertedElements = ConvertAndBake(atomicObject);
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

  private List<BDE.Element> ConvertAndBake(Base atomicObject)
  {
    var baked = new List<BDE.Element>();
    object converted = _converter.Convert(atomicObject);

    switch (converted)
    {
      case BDE.Element element:
        AddToModel(element, baked);
        break;

      // data object conversions return element/base pairs
      case IEnumerable<(BDE.Element, Base)> typedList:
        foreach (var (element, _) in typedList)
        {
          AddToModel(element, baked);
        }
        break;

      // display value fallback conversions are zipped as (object, Base) pairs by ConverterWithFallback
      case IEnumerable<(object, Base)> fallbackList:
        foreach (var (obj, _) in fallbackList)
        {
          if (obj is BDE.Element element)
          {
            AddToModel(element, baked);
          }
        }
        break;

      case IEnumerable<BDE.Element> elements:
        foreach (var element in elements)
        {
          AddToModel(element, baked);
        }
        break;

      default:
        throw new SpeckleConversionException(
          $"Conversion of {atomicObject.speckle_type} returned an unexpected result: {converted?.GetType().Name}"
        );
    }

    return baked;
  }

  private static void AddToModel(BDE.Element element, List<BDE.Element> baked)
  {
    var status = element.AddToModel();
    if (status == BDPN.StatusInt.Error)
    {
      throw new SpeckleConversionException("Failed to add element to the model.");
    }

    baked.Add(element);
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
