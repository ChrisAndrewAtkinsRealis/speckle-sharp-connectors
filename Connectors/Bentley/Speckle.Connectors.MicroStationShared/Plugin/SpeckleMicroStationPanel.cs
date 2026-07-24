using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
#if OPENROADS || OPENRAIL
using Speckle.Converters.OpenRoads;
#else
using Speckle.Converters.MicroStation;
#endif

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// A modeless WinForms window that hosts the DUI3 WebView2 panel (a WPF control) via an <see cref="ElementHost"/>.
/// The DI container is built the first time the panel is opened and reused afterwards.
/// </summary>
public sealed class SpeckleMicroStationPanel : Form
{
  private static SpeckleMicroStationPanel? s_instance;
  private static ServiceProvider? s_container;

  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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

    var webview = ServiceContainer.GetRequiredService<DUI3ControlWebView>();
    var host = new ElementHost { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(host);

    FormClosing += OnFormClosing;

    // parent this window to MicroStation's main window so it stays on top of the host
    IntPtr mainWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
    if (mainWindow != IntPtr.Zero)
    {
      SetWindowLongPtr(Handle, GWL_HWNDPARENT, mainWindow);
    }
  }

  private void OnFormClosing(object? sender, FormClosingEventArgs e)
  {
    if (e.CloseReason == CloseReason.UserClosing)
    {
      e.Cancel = true;
      Hide();
    }
  }

  private static ServiceProvider ServiceContainer => s_container ??= BuildContainer();

  private static ServiceProvider BuildContainer()
  {
    var services = new ServiceCollection();
    services.Initialize(GetHostApplication(), GetVersion());
    services.AddMicroStation();

#if OPENROADS || OPENRAIL
    // registers the MicroStation geometry converters AND the civil converters (same compiled assembly)
    services.AddOpenRoadsConverters();
#else
    services.AddMicroStationConverters();
#endif

    var container = services.BuildServiceProvider();
    container.UseDUI();
    return container;
  }

  private static Speckle.Sdk.Application GetHostApplication() =>
#if OPENROADS
    HostApplications.OpenRoads;
#elif OPENRAIL
    HostApplications.OpenRail;
#else
    HostApplications.MicroStation;
#endif

  /// <summary>
  /// Opens the Speckle panel, or brings the existing one to the front.
  /// </summary>
  public static void CreateOrFocus()
  {
    if (s_instance is { IsDisposed: false })
    {
      if (!s_instance.Visible)
      {
        s_instance.Show();
      }

      s_instance.BringToFront();
      s_instance.Focus();
      s_instance.Activate();
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
#if MICROSTATION2026 || OPENROADS2026 || OPENRAIL2026
    HostAppVersion.v2026;
#else
    throw new NotSupportedException("Unsupported Bentley host version.");
#endif
}
