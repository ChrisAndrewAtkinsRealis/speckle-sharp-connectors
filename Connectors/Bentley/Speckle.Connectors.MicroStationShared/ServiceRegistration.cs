using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.MicroStation.Bindings;
using Speckle.Connectors.MicroStation.Filters;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Connectors.MicroStation.Operations.Receive;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.MicroStation;

public static class ServiceRegistration
{
  public static IServiceCollection AddMicroStation(this IServiceCollection services)
  {
    var connectorAssembly = Assembly.GetExecutingAssembly();

    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectors();
    services.AddDUI<DefaultThreadContext, MicroStationDocumentModelStore>();
    services.AddDUIView();

    // host app services
    services.AddSingleton<MicroStationReferenceService>();
    services.AddSingleton<MicroStationContext>();
    services.AddSingleton<IAppIdleManager, MicroStationIdleManager>();
    services.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    // stock bindings
    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    // connector bindings
    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, MicroStationBasicConnectorBinding>();
    services.AddSingleton<IBinding, MicroStationSelectionBinding>();
    services.AddSingleton<IBinding, MicroStationSendBinding>();
    services.AddSingleton<IBinding, MicroStationReceiveBinding>();

    // instances (cells / shared cells / parametric cells)
    services.AddScoped<
      IInstanceObjectsManager<MicroStationRootObject, List<BDE.Element>>,
      InstanceObjectsManager<MicroStationRootObject, List<BDE.Element>>
    >();
    services.AddScoped<MicroStationInstanceUnpacker>();
    services.AddScoped<MicroStationInstanceBaker>();

    // send
    services.AddScoped<ISendFilter, MicroStationSelectionFilter>();
    services.AddSingleton<ISendConversionCache, SendConversionCache>();
    services.AddScoped<IRootObjectBuilder<MicroStationRootObject>, MicroStationRootObjectBuilder>();
    services.AddScoped<SendOperation<MicroStationRootObject>>();

    // receive
    services.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    services.AddScoped<MicroStationLevelBaker>();
    services.AddScoped<IHostObjectBuilder, MicroStationHostObjectBuilder>();

    services.AddMatchingInterfacesAsTransient(connectorAssembly);

    return services;
  }
}
