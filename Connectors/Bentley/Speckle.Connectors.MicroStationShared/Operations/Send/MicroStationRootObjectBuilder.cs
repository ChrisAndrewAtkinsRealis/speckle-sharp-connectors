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
  private readonly MicroStationReferenceService _referenceService;
  private readonly MicroStationContext _context;
  private readonly ILogger<MicroStationRootObjectBuilder> _logger;

  // reference (attachment id) -> its collection; and "{parentKey}:{levelName}" -> level collection under that parent
  private readonly Dictionary<string, Collection> _referenceCollections = new();
  private readonly Dictionary<string, Collection> _levelCollections = new();

  public MicroStationRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
    ISendConversionCache sendConversionCache,
    MicroStationInstanceUnpacker instanceUnpacker,
    MicroStationReferenceService referenceService,
    MicroStationContext context,
    ILogger<MicroStationRootObjectBuilder> logger
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _sendConversionCache = sendConversionCache;
    _instanceUnpacker = instanceUnpacker;
    _referenceService = referenceService;
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
    var settings = _converterSettings.Current;
    string fileName = _context.ActiveFileName ?? "MicroStation Model";

    // the root represents the active model (a DGN file can contain many models); record its identity so the
    // source structure - which model, of what type and dimensionality - is preserved.
    Collection root = new() { name = settings.ModelName };
    root["units"] = settings.SpeckleUnits;
    root["fileName"] = System.IO.Path.GetFileName(fileName);
    root["modelName"] = settings.ModelName;
    root["modelType"] = settings.ModelType;
    root["dimension"] = settings.Is3d ? "3D" : "2D";

    // 1 - unpack cells into instance proxies + definitions (shared/normal/parametric)
    var unpacked = _instanceUnpacker.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = unpacked.InstanceDefinitionProxies;

    // 2 - convert atomic objects; cell placements are emitted as their instance proxy rather than converted
    var results = new List<SendConversionResult>(unpacked.AtomicObjects.Count);
    int count = 0;

    foreach (var (element, applicationId) in unpacked.AtomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var targetCollection = GetTargetCollection(element, applicationId, root);
      results.Add(ConvertOrProxy(element, applicationId, targetCollection, unpacked.InstanceProxies, projectId));

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

  /// <summary>
  /// Returns the collection an element belongs in, preserving the source structure: active-model elements are
  /// grouped by level directly under the root; reference elements are grouped by level under a per-reference
  /// collection named after the source file (e.g. "x.dgn").
  /// </summary>
  private Collection GetTargetCollection(BDE.Element element, string applicationId, Collection root)
  {
    Collection parent = root;
    string parentKey = "root";
    BDPN.DgnModel levelModel = _converterSettings.Current.Model;

    if (MicroStationReferenceService.TryGetAttachmentId(applicationId, out ulong attachmentId))
    {
      var info = _referenceService.GetReferenceInfo(_converterSettings.Current.Model, attachmentId);
      parent = GetOrCreateReferenceCollection(attachmentId, info, root);
      parentKey = "R" + attachmentId;
      // reference element levels live in the reference model, not the active one
      levelModel = info?.Model ?? levelModel;
    }

    string levelName = GetLevelName(element, levelModel);
    string levelKey = $"{parentKey}:{levelName}";
    if (_levelCollections.TryGetValue(levelKey, out Collection? levelCollection))
    {
      return levelCollection;
    }

    levelCollection = new Collection { name = levelName };
    _levelCollections[levelKey] = levelCollection;
    parent.elements.Add(levelCollection);
    return levelCollection;
  }

  private Collection GetOrCreateReferenceCollection(
    ulong attachmentId,
    MicroStationReferenceService.ReferenceInfo? info,
    Collection root
  )
  {
    string key = attachmentId.ToString();
    if (_referenceCollections.TryGetValue(key, out Collection? collection))
    {
      return collection;
    }

    collection = new Collection { name = info?.Name ?? $"Reference {attachmentId}" };
    collection["isReference"] = true;
    _referenceCollections[key] = collection;
    root.elements.Add(collection);
    return collection;
  }

  private string GetLevelName(BDE.Element element, BDPN.DgnModel model)
  {
    try
    {
      var levelCache = model.GetFileLevelCache();
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
