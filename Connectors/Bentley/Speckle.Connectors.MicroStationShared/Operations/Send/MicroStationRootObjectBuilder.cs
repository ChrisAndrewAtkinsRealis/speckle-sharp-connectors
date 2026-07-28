using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Objects.Geometry;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

public class MicroStationRootObjectBuilder : IRootObjectBuilder<MicroStationRootObject>
{
  private static readonly bool s_verboseElementLogging = IsVerboseElementLoggingEnabled();

  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _converterSettings;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly MicroStationInstanceUnpacker _instanceUnpacker;
  private readonly MicroStationReferenceService _referenceService;
  private readonly MicroStationColorUnpacker _colorUnpacker;
  private readonly ICivilModelContributor _civilModelContributor;
  private readonly MicroStationContext _context;
  private readonly ILogger<MicroStationRootObjectBuilder> _logger;

  // reference (attachment id) -> its collection; and "{parentKey}:{levelName}" -> level collection under that parent
  private readonly Dictionary<string, Collection> _referenceCollections = new();
  private readonly Dictionary<string, Collection> _levelCollections = new();

  public MicroStationRootObjectBuilder(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
    ISendConversionCache sendConversionCache,
    MicroStationInstanceUnpacker instanceUnpacker,
    MicroStationReferenceService referenceService,
    MicroStationColorUnpacker colorUnpacker,
    ICivilModelContributor civilModelContributor,
    MicroStationContext context,
    ILogger<MicroStationRootObjectBuilder> logger
  )
  {
    _toSpeckle = toSpeckle;
    _converterSettings = converterSettings;
    _sendConversionCache = sendConversionCache;
    _instanceUnpacker = instanceUnpacker;
    _referenceService = referenceService;
    _colorUnpacker = colorUnpacker;
    _civilModelContributor = civilModelContributor;
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
    SpeckleMicroStationPanel.LogPanelInfo($"Send build started. Objects={objects.Count}, ProjectId={projectId}.");

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
    SpeckleMicroStationPanel.LogPanelInfo("Selection unpack started.");
    var unpackStopwatch = System.Diagnostics.Stopwatch.StartNew();
    var unpacked = _instanceUnpacker.UnpackSelection(objects);
    unpackStopwatch.Stop();
    root[ProxyKeys.INSTANCE_DEFINITION] = unpacked.InstanceDefinitionProxies;

    SpeckleMicroStationPanel.LogPanelInfo(
      $"Selection unpacked in {unpackStopwatch.Elapsed.TotalSeconds:F1}s. AtomicObjects={unpacked.AtomicObjects.Count}, InstanceDefinitions={unpacked.InstanceDefinitionProxies.Count}, InstanceProxies={unpacked.InstanceProxies.Count}."
    );
    if (unpacked.AtomicObjects.Count > 5000)
    {
      SpeckleMicroStationPanel.LogPanelInfo(
        "Large send detected. Consider disabling item-level logs via SPECKLE_MICROSTATION_VERBOSE_SEND_LOGS=0 and narrowing selection where possible."
      );
    }

    // 2 - convert atomic objects; cell placements are emitted as their instance proxy rather than converted
    var results = new List<SendConversionResult>(unpacked.AtomicObjects.Count);
    var sourceTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    var outputTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    int count = 0;

    foreach (var (element, applicationId) in unpacked.AtomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var targetCollection = GetTargetCollection(element, applicationId, root);
      var result = ConvertOrProxy(element, applicationId, targetCollection, unpacked.InstanceProxies, projectId);
      results.Add(result);

      string sourceType = element.GetType().Name;
      IncrementCount(sourceTypeCounts, sourceType);
      if (result.Result is not null)
      {
        IncrementCount(outputTypeCounts, GetOutputTypeName(result.Result));
      }

      onOperationProgressed.Report(new("Converting", (double)++count / unpacked.AtomicObjects.Count));
    }

    // 3 - unpack element colours so appearance survives in other apps
    root[ProxyKeys.COLOR] = _colorUnpacker.UnpackColors(unpacked.AtomicObjects);

    // 4 - contribute civil entities (no-op for plain MicroStation; OpenRoads/OpenRail add a "Civil" collection)
    results.AddRange(_civilModelContributor.Contribute(root, cancellationToken));

    int successCount = results.Count(x => x.Status == Status.SUCCESS);
    int errorCount = results.Count(x => x.Status == Status.ERROR);
    SpeckleMicroStationPanel.LogPanelInfo(
      $"Send build finished conversion phase. Success={successCount}, Errors={errorCount}, TotalResults={results.Count}."
    );
    SpeckleMicroStationPanel.LogPanelInfo($"Type coverage summary (source): {FormatCounts(sourceTypeCounts)}");
    SpeckleMicroStationPanel.LogPanelInfo($"Type coverage summary (output): {FormatCounts(outputTypeCounts)}");

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      SpeckleMicroStationPanel.LogPanelError("All converted objects failed in send pipeline.");
      throw new SpeckleException("Failed to convert all objects.");
    }

    SpeckleMicroStationPanel.LogPanelInfo("Send build completed successfully.");
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
    if (s_verboseElementLogging)
    {
      SpeckleMicroStationPanel.LogPanelInfo($"Converting element. ApplicationId={applicationId}, SourceType={sourceType}.");
    }

    try
    {
      Base converted;
      if (instanceProxies.TryGetValue(applicationId, out InstanceProxy? instanceProxy))
      {
        // this element is a cell placement: emit the instance proxy in place of converted geometry
        converted = instanceProxy;
        if (s_verboseElementLogging)
        {
          SpeckleMicroStationPanel.LogPanelInfo(
            $"Element emitted as instance proxy. ApplicationId={applicationId}, SourceType={sourceType}."
          );
        }
      }
      else if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? cached))
      {
        converted = cached;
        if (s_verboseElementLogging)
        {
          SpeckleMicroStationPanel.LogPanelInfo(
            $"Element resolved from conversion cache. ApplicationId={applicationId}, SourceType={sourceType}."
          );
        }
      }
      else
      {
        var objectConverter = _toSpeckle.ResolveConverter(element.GetType());
        converted = objectConverter.Convert(element);
        converted.applicationId = applicationId;
        if (s_verboseElementLogging)
        {
          SpeckleMicroStationPanel.LogPanelInfo(
            $"Element converted via top-level converter. ApplicationId={applicationId}, SourceType={sourceType}."
          );
        }
      }

      levelCollection.elements.Add(converted);
      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert MicroStation element {SourceType}", sourceType);
      SpeckleMicroStationPanel.LogPanelError(
        $"Failed to convert element. ApplicationId={applicationId}, SourceType={sourceType}.",
        ex
      );
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
    ReferenceInfo? info,
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

  private static string GetOutputTypeName(Base converted) =>
    converted switch
    {
      SolidX => "SolidX",
      Mesh => "Mesh",
      Line => "Line",
      _ when converted.GetType().Name == "DataObject" => "DataObject",
      _ => converted.GetType().Name,
    };

  private static void IncrementCount(IDictionary<string, int> counts, string key)
  {
    counts[key] = counts.TryGetValue(key, out int current) ? current + 1 : 1;
  }

  private static string FormatCounts(IReadOnlyDictionary<string, int> counts)
  {
    if (counts.Count == 0)
    {
      return "none";
    }

    return string.Join(", ", counts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={kvp.Value}"));
  }

  private static bool IsVerboseElementLoggingEnabled()
  {
    string? value = Environment.GetEnvironmentVariable("SPECKLE_MICROSTATION_VERBOSE_SEND_LOGS");
    return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
      || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
      || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
  }
}
