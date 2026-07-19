using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

public class MicroStationRootObjectBuilder : IRootObjectBuilder<MicroStationRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _converterSettings;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly MicroStationInstanceUnpacker _instanceUnpacker;
  private readonly MicroStationContext _context;
  private readonly ILogger<MicroStationRootObjectBuilder> _logger;
  private readonly Dictionary<string, Collection> _levelCollections = new();

  public MicroStationRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
    ISendConversionCache sendConversionCache,
    MicroStationInstanceUnpacker instanceUnpacker,
    MicroStationContext context,
    ILogger<MicroStationRootObjectBuilder> logger
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _sendConversionCache = sendConversionCache;
    _instanceUnpacker = instanceUnpacker;
    _context = context;
    _logger = logger;
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<MicroStationRootObject> objects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    string fileName = _context.ActiveFileName ?? "MicroStation Model";
    Collection root = new() { name = System.IO.Path.GetFileNameWithoutExtension(fileName) };
    root["units"] = _converterSettings.Current.SpeckleUnits;

    // 1 - unpack cells into instance proxies + definitions (shared/normal/parametric)
    var unpacked = _instanceUnpacker.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = unpacked.InstanceDefinitionProxies;

    // 2 - convert atomic objects; cell placements are emitted as their instance proxy rather than converted
    var results = new List<SendConversionResult>(unpacked.AtomicObjects.Count);
    int count = 0;

    foreach (var (element, applicationId) in unpacked.AtomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var levelCollection = GetOrCreateLevelCollection(element, root);
      results.Add(ConvertOrProxy(element, applicationId, levelCollection, unpacked.InstanceProxies, projectId));

      onOperationProgressed.Report(new("Converting", (double)++count / unpacked.AtomicObjects.Count));
    }

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return Task.FromResult(new RootObjectBuilderResult(root, results));
  }

  private SendConversionResult ConvertOrProxy(
    BDE.Element element,
    string applicationId,
    Collection levelCollection,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies,
    string projectId
  )
  {
    string sourceType = element.GetType().Name;
    try
    {
      Base converted;
      if (instanceProxies.TryGetValue(applicationId, out InstanceProxy? instanceProxy))
      {
        // this element is a cell placement: emit the instance proxy in place of converted geometry
        converted = instanceProxy;
      }
      else if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? cached))
      {
        converted = cached;
      }
      else
      {
        converted = _converter.Convert(element);
        converted.applicationId = applicationId;
      }

      levelCollection.elements.Add(converted);
      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert MicroStation element {SourceType}", sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }

  private Collection GetOrCreateLevelCollection(BDE.Element element, Collection root)
  {
    string levelName = GetLevelName(element);
    if (_levelCollections.TryGetValue(levelName, out Collection? collection))
    {
      return collection;
    }

    collection = new Collection { name = levelName };
    _levelCollections[levelName] = collection;
    root.elements.Add(collection);
    return collection;
  }

  private string GetLevelName(BDE.Element element)
  {
    try
    {
      var levelCache = _converterSettings.Current.Model.GetFileLevelCache();
      var level = levelCache.GetLevel(element.LevelId);
      string? name = level?.Name;
      return string.IsNullOrEmpty(name) ? "Default" : name!;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to resolve level name for element, using Default");
      return "Default";
    }
  }
}
