using Bentley.DgnPlatformNET.DgnEC;
using Bentley.EC.Persistence.Query;
using Bentley.ECObjects.Instance;
using Bentley.ECObjects;
using Bentley.DgnPlatformNET;
using Bentley.ECObjects.Schema;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Persists DUI3 model cards into the DGN file using an EC schema (the mechanism the v2 connector used for
/// its stream states). A DGN file can contain many models and element ids are only unique within a model, so
/// cards are stored per active model: the EC property holds a json envelope of <c>{ modelKey: cardsJson }</c>.
/// </summary>
public class MicroStationDocumentModelStore : DocumentModelStore
{
  private const string SCHEMA_NAME = "SpeckleDUI3StateWrapper";
  private const string CLASS_NAME = "SpeckleDUI3State";
  private const string PROPERTY_NAME = "SpeckleDUI3Data";

  private readonly MicroStationContext _context;
  private readonly ILogger<MicroStationDocumentModelStore> _logger;

  public MicroStationDocumentModelStore(
    ILogger<DocumentModelStore> baseLogger,
    IJsonSerializer jsonSerializer,
    MicroStationContext context,
    ILogger<MicroStationDocumentModelStore> logger
  )
    : base(baseLogger, jsonSerializer)
  {
    _context = context;
    _logger = logger;

    if (_context.ActiveFile is not null)
    {
      IsDocumentInit = true;
      LoadState();
    }
  }

  /// <summary>
  /// Called by the add-in when the active design file changes.
  /// </summary>
  public void OnDocumentSwap()
  {
    IsDocumentInit = _context.ActiveFile is not null;
    LoadState();
    OnDocumentChanged();
  }

  protected override void LoadState()
  {
    var file = _context.ActiveFile;
    if (file is null)
    {
      ClearAndSave();
      return;
    }

    try
    {
      var envelope = DeserializeEnvelope(ReadRawState(file));
      envelope.TryGetValue(_context.ActiveModelKey, out string? modelCards);
      LoadFromString(modelCards ?? string.Empty);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to load Speckle state from DGN file, initializing empty state");
      ClearAndSave();
    }
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    var file = _context.ActiveFile;
    if (file is null)
    {
      return;
    }

    try
    {
      // preserve other models' cards: read the envelope, update only the active model's bucket
      var envelope = DeserializeEnvelope(ReadRawState(file));
      envelope[_context.ActiveModelKey] = modelCardState;
      WriteRawState(file, SerializeEnvelope(envelope));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to save Speckle state to DGN file");
    }
  }

  private static Dictionary<string, string> DeserializeEnvelope(string? raw)
  {
    if (string.IsNullOrEmpty(raw))
    {
      return new Dictionary<string, string>();
    }

    try
    {
      return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw!)
        ?? new Dictionary<string, string>();
    }
    catch (System.Text.Json.JsonException)
    {
      // not an envelope (or corrupt) - start clean rather than lose the file
      return new Dictionary<string, string>();
    }
  }

  private static string SerializeEnvelope(Dictionary<string, string> envelope) =>
    System.Text.Json.JsonSerializer.Serialize(envelope);

  private DgnECManager? TryGetEcManager()
  {
    try
    {
      return DgnECManager.Manager;
    }
    catch (NullReferenceException ex)
    {
      // Bentley host object can be unavailable during early add-in startup.
      _logger.LogDebug(ex, "DgnECManager is not available yet");
      return null;
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogDebug(ex, "DgnECManager is not available yet");
      return null;
    }
  }

  private string? ReadRawState(BDPN.DgnFile file)
  {
    DgnECManager? manager = TryGetEcManager();
    if (manager is null)
    {
      return null;
    }

    var scope = FindInstancesScope.CreateScope(file, new FindInstancesScopeOption(DgnECHostType.All));
    var schema = (ECSchema?)manager.LocateSchemaInScope(scope, SCHEMA_NAME, 1, 0, SchemaMatchType.Latest);
    if (schema is null)
    {
      return null;
    }

    var query = new ECQuery(schema.GetClass(CLASS_NAME));
    query.SelectClause.SelectAllProperties = true;

    using DgnECInstanceCollection instances = manager.FindInstances(scope, query);
    return instances.FirstOrDefault()?[PROPERTY_NAME].StringValue;
  }

  private void WriteRawState(BDPN.DgnFile file, string rawState)
  {
    DgnECManager? manager = TryGetEcManager();
    if (manager is null)
    {
      _logger.LogWarning("Skipping Speckle state save because DgnECManager is not available");
      return;
    }

    var scope = FindInstancesScope.CreateScope(file, new FindInstancesScopeOption(DgnECHostType.All));

    IECSchema schema = RetrieveOrCreateSchema(manager, file, scope);
    IECClass ecClass = schema.GetClass(CLASS_NAME);

    // delete any existing state instances before writing the current state
    var query = new ECQuery(ecClass);
    query.SelectClause.SelectAllProperties = true;
    using (DgnECInstanceCollection instances = manager.FindInstances(scope, query))
    {
      foreach (IDgnECInstance instance in instances)
      {
        instance.Delete();
      }
    }

    DgnECInstanceEnabler instanceEnabler = manager.ObtainInstanceEnabler(file, ecClass);
    StandaloneECDInstance ecInstance = instanceEnabler.SharedWipInstance;
    ecInstance.SetAsString(PROPERTY_NAME, rawState);
    instanceEnabler.CreateInstanceOnFile(file, ecInstance);
  }

  private static IECSchema RetrieveOrCreateSchema(DgnECManager manager, BDPN.DgnFile file, FindInstancesScope scope)
  {
    IECSchema? schema = manager.LocateSchemaInScope(scope, SCHEMA_NAME, 1, 0, SchemaMatchType.Latest);
    if (schema is not null)
    {
      return schema;
    }

    var newSchema = new ECSchema(SCHEMA_NAME, 1, 0, SCHEMA_NAME);
    var stateClass = new ECClass(CLASS_NAME);
    stateClass.Add(new ECProperty(PROPERTY_NAME, ECObjects.StringType));
    newSchema.AddClass(stateClass);

    SchemaImportStatus status = manager.ImportSchema(newSchema, file, new ImportSchemaOptions());
    if (status != SchemaImportStatus.Success)
    {
      throw new InvalidOperationException($"Failed to import Speckle EC schema into DGN file: {status}");
    }

    return newSchema;
  }
}
