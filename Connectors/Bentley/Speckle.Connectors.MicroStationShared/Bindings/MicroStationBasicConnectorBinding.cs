using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.MicroStation.Bindings;

public class MicroStationBasicConnectorBinding : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  public BasicConnectorBindingCommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly IAccountManager _accountManager;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly MicroStationContext _context;
  private readonly IThreadContext _threadContext;
  private readonly ILogger<MicroStationBasicConnectorBinding> _logger;

  public MicroStationBasicConnectorBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IAccountManager accountManager,
    ISpeckleApplication speckleApplication,
    MicroStationContext context,
    IThreadContext threadContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    ILogger<MicroStationBasicConnectorBinding> logger
  )
  {
    _store = store;
    Parent = parent;
    _accountManager = accountManager;
    _speckleApplication = speckleApplication;
    _context = context;
    _threadContext = threadContext;
    _logger = logger;
    Commands = new BasicConnectorBindingCommands(parent);

    _store.DocumentChanged += (_, _) =>
      topLevelExceptionHandler.FireAndForget(async () => await Commands.NotifyDocumentChanged());
  }

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public Account[] GetAccounts() => _accountManager.GetAccounts().ToArray();

  public DocumentInfo? GetDocumentInfo()
  {
    var file = _context.ActiveFile;
    if (file is null)
    {
      return null;
    }

    string path = file.GetFileName();
    string name = System.IO.Path.GetFileName(path);
    return new DocumentInfo(path, name, path.GetHashCode().ToString());
  }

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.AddModel(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public void RemoveModels(List<ModelCard> models) => _store.RemoveModels(models);

  public async Task HighlightObjects(IReadOnlyList<string> objectIds) =>
    await HighlightObjectsOnView(objectIds);

  public async Task HighlightModel(string modelCardId)
  {
    var model = _store.GetModelById(modelCardId);
    if (model is null)
    {
      _logger.LogError("Model was null when highlighting received model");
      return;
    }

    var objectIds = model switch
    {
      SenderModelCard sender => sender.SendFilter.NotNull().RefreshObjectIds(),
      ReceiverModelCard receiver => receiver.BakedObjectIds.NotNull(),
      _ => new List<string>(),
    };

    if (objectIds.Count == 0)
    {
      await Commands.SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."));
      return;
    }

    await HighlightObjectsOnView(objectIds);
  }

  private async Task HighlightObjectsOnView(IReadOnlyList<string> objectIds) =>
    await _threadContext.RunOnMainAsync(() =>
    {
      try
      {
        var activeModelRef = BMPN.Session.Instance.GetActiveDgnModelRef();
        BMPN.SelectionSetManager.EmptyAll();

        foreach (string objectId in objectIds)
        {
          // reference elements must be added against their own model ref (the attachment), not the active one
          var (element, modelRef) = _context.FindElementWithModelRef(objectId);
          if (element is not null)
          {
            BMPN.SelectionSetManager.AddElement(element, modelRef ?? activeModelRef);
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Failed to highlight objects in MicroStation");
      }

      return Task.CompletedTask;
    });
}
