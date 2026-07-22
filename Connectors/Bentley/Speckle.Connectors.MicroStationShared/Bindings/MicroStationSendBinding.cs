using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Connectors.MicroStation.Operations.Send.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.MicroStation.Bindings;

public sealed class MicroStationSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  public SendBindingUICommands Commands { get; }

  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;
  private readonly IMicroStationConversionSettingsFactory _conversionSettingsFactory;
  private readonly MicroStationContext _context;
  private readonly IThreadContext _threadContext;

  public MicroStationSendBinding(
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendOperationManagerFactory sendOperationManagerFactory,
    IMicroStationConversionSettingsFactory conversionSettingsFactory,
    MicroStationContext context,
    IThreadContext threadContext
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    _conversionSettingsFactory = conversionSettingsFactory;
    _context = context;
    _threadContext = threadContext;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [new IncludeReferencesSetting()];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (serviceProvider, _) =>
        serviceProvider
          .GetRequiredService<IConverterSettingsStore<MicroStationConversionSettings>>()
          .Initialize(
            _conversionSettingsFactory.Create(_context.ActiveFile.NotNull(), _context.ActiveModel.NotNull())
          ),
      GatherObjects,
      _context.ActiveFileName,
      null
    );
  }

  private IReadOnlyList<MicroStationRootObject> GatherObjects(SenderModelCard card)
  {
    bool includeReferences =
      card.Settings?.FirstOrDefault(s => s.Id == IncludeReferencesSetting.SETTING_ID)?.Value as bool? ?? false;

    var objects = new List<MicroStationRootObject>();
    foreach (string id in card.SendFilter.NotNull().RefreshObjectIds())
    {
      // skip reference elements unless the user opted in
      if (!includeReferences && MicroStationReferenceService.IsReferenceId(id))
      {
        continue;
      }

      var element = _context.FindElement(id);
      if (element is not null)
      {
        objects.Add(new MicroStationRootObject(element, id));
      }
    }

    return objects;
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
