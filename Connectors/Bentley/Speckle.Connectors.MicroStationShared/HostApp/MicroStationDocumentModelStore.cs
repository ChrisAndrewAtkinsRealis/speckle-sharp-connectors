using Bentley.DgnPlatformNET.DgnEC;
using Bentley.EC.Persistence.Query;
using Bentley.ECObjects;
using Bentley.ECObjects.Instance;
using Bentley.ECObjects.Schema;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Persists DUI3 model cards into the DGN file using an EC schema, the same mechanism the v2
/// connector used for its stream states. State is stored as a single json string property.
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
      var scope = FindInstancesScope.CreateScope(file, new FindInstancesScopeOption(DgnECHostType.All));
      var schema = (ECSchema?)
        DgnECManager.Manager.LocateSchemaInScope(scope, SCHEMA_NAME, 1, 0, SchemaMatchType.Latest);

      if (schema is null)
      {
        ClearAndSave();
        return;
      }

      var query = new ECQuery(schema.GetClass(CLASS_NAME));
      query.SelectClause.SelectAllProperties = true;

      using DgnECInstanceCollection instances = DgnECManager.Manager.FindInstances(scope, query);
      var stateInstance = instances.FirstOrDefault();
      if (stateInstance is null)
      {
        ClearAndSave();
        return;
      }

      LoadFromString(stateInstance[PROPERTY_NAME].StringValue);
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
      DgnECManager manager = DgnECManager.Manager;
      var scope = FindInstancesScope.CreateScope(file, new FindInstancesScopeOption(DgnECHostType.All));

      IECSchema schema = RetrieveOrCreateSchema(file, scope);
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
      StandaloneECDInstance instance = instanceEnabler.SharedWipInstance;
      instance.SetAsString(PROPERTY_NAME, modelCardState);
      instanceEnabler.CreateInstanceOnFile(file, instance);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to save Speckle state to DGN file");
    }
  }

  private static IECSchema RetrieveOrCreateSchema(BDPN.DgnFile file, FindInstancesScope scope)
  {
    IECSchema? schema = DgnECManager.Manager.LocateSchemaInScope(scope, SCHEMA_NAME, 1, 0, SchemaMatchType.Latest);
    if (schema is not null)
    {
      return schema;
    }

    var newSchema = new ECSchema(SCHEMA_NAME, 1, 0, SCHEMA_NAME);
    var stateClass = new ECClass(CLASS_NAME);
    stateClass.Add(new ECProperty(PROPERTY_NAME, ECObjects.StringType));
    newSchema.AddClass(stateClass);

    SchemaImportStatus status = DgnECManager.Manager.ImportSchema(newSchema, file, new ImportSchemaOptions());
    if (status != SchemaImportStatus.Success)
    {
      throw new InvalidOperationException($"Failed to import Speckle EC schema into DGN file: {status}");
    }

    return newSchema;
  }
}
