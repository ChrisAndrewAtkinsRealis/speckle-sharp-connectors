using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

public class MicroStationRootObjectBuilder : IRootObjectBuilder<MicroStationRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _converterSettings;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly MicroStationContext _context;
  private readonly ILogger<MicroStationRootObjectBuilder> _logger;
  private readonly Dictionary<string, Collection> _levelCollections = new();

  public MicroStationRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
    ISendConversionCache sendConversionCache,
    MicroStationContext context,
    ILogger<MicroStationRootObjectBuilder> logger
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _sendConversionCache = sendConversionCache;
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

    var results = new List<SendConversionResult>(objects.Count);
    int count = 0;

    foreach (var (element, applicationId) in objects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      string sourceType = element.GetType().Name;
      try
      {
        Base converted;
        if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? cached))
        {
          converted = cached;
        }
        else
        {
          converted = _converter.Convert(element);
          converted.applicationId = applicationId;
        }

        // group converted objects into a collection per level, mirroring how other cad connectors group by layer
        GetOrCreateLevelCollection(element, root).elements.Add(converted);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to convert MicroStation element {SourceType}", sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
      }

      onOperationProgressed.Report(new("Converting", (double)++count / objects.Count));
    }

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return Task.FromResult(new RootObjectBuilderResult(root, results));
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
