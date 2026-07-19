using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// A modeless WinForms window that hosts the DUI3 WebView2 panel (a WPF control) via an <see cref="ElementHost"/>.
/// The DI container is built the first time the panel is opened and reused afterwards.
/// </summary>
public sealed class SpeckleMicroStationPanel : Form
{
  private static SpeckleMicroStationPanel? s_instance;
  private static ServiceProvider? s_container;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

  private const int GWL_HWNDPARENT = -8;

  private SpeckleMicroStationPanel()
  {
    Text = "Speckle";
    Name = "Speckle";
    Width = 400;
    Height = 700;
    StartPosition = FormStartPosition.CenterScreen;

    var webview = Container.GetRequiredService<DUI3ControlWebView>();
    var host = new ElementHost { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(host);

    // parent this window to MicroStation's main window so it stays on top of the host
    IntPtr mainWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
    if (mainWindow != IntPtr.Zero)
    {
      SetWindowLongPtr(Handle, GWL_HWNDPARENT, mainWindow);
    }
  }

  private static ServiceProvider Container => s_container ??= BuildContainer();

  private static ServiceProvider BuildContainer()
  {
    var services = new ServiceCollection();
    services.Initialize(HostApplications.MicroStation, GetVersion());
    services.AddMicroStation();
    services.AddMicroStationConverters();

    var container = services.BuildServiceProvider();
    container.UseDUI();
    return container;
  }

  /// <summary>
  /// Opens the Speckle panel, or brings the existing one to the front.
  /// </summary>
  public static void CreateOrFocus()
  {
    if (s_instance is { IsDisposed: false })
    {
      s_instance.BringToFront();
      s_instance.Focus();
      return;
    }

    s_instance = new SpeckleMicroStationPanel();
    s_instance.Show();
    s_instance.Activate();
  }

  protected override void OnFormClosed(FormClosedEventArgs e)
  {
    base.OnFormClosed(e);
    s_instance = null;
  }

  private static HostAppVersion GetVersion() =>
#if MICROSTATION2026
    HostAppVersion.v2026;
#else
    throw new NotSupportedException("Unsupported MicroStation version.");
#endif
}
