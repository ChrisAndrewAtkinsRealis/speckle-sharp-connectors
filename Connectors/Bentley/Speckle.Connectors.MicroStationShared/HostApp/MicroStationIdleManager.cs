using System.Windows.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// MicroStation's managed API does not expose a dedicated application idle event, so deferred
/// actions are scheduled on the main thread dispatcher at idle priority.
/// </summary>
public sealed class MicroStationIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;
  private readonly Dispatcher _dispatcher;

  public MicroStationIdleManager(IIdleCallManager idleCallManager)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
    // the container is built on the MicroStation main thread, so this captures the main dispatcher
    _dispatcher = Dispatcher.CurrentDispatcher;
  }

  protected override void AddEvent() =>
    _dispatcher.BeginInvoke(
      DispatcherPriority.ApplicationIdle,
      new Action(() => _idleCallManager.AppOnIdle(() => { }))
    );
}
