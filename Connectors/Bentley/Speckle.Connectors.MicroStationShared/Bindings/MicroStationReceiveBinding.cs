using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.MicroStation.Bindings;

public sealed class MicroStationReceiveBinding(
  IBrowserBridge parent,
  ICancellationManager cancellationManager,
  IThreadContext threadContext,
  IReceiveOperationManagerFactory receiveOperationManagerFactory,
  IMicroStationConversionSettingsFactory conversionSettingsFactory,
  MicroStationContext context
) : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; } = parent;

  private ReceiveBindingUICommands Commands { get; } = new(parent);

  public void CancelReceive(string modelCardId) => cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId) =>
    await threadContext.RunOnMainAsync(async () => await ReceiveInternal(modelCardId));

  private async Task ReceiveInternal(string modelCardId)
  {
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      InitializeSettings,
      async (_, processor) => await processor()
    );
  }

  private void InitializeSettings(IServiceProvider serviceProvider, ModelCard modelCard) =>
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<MicroStationConversionSettings>>()
      .Initialize(conversionSettingsFactory.Create(context.ActiveFile.NotNull(), context.ActiveModel.NotNull()));
}
