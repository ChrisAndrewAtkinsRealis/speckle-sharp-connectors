using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Sdk;
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
  private static readonly string s_bootstrapLogPath = BuildBootstrapLogPath();
  private static bool s_bootstrapLogInitialized;

  internal static void LogPanelInfo(string message) => WriteBootstrapLog("INFO", message, null);

  internal static void LogPanelError(string message, Exception? ex = null) => WriteBootstrapLog("ERROR", message, ex);

  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

  private const int GWL_HWNDPARENT = -8;

  private SpeckleMicroStationPanel()
  {
    LogPanelInfo("Constructing SpeckleMicroStationPanel.");

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
      LogPanelInfo("Panel parented to MicroStation main window.");
    }
    else
    {
      LogPanelInfo("MicroStation main window handle was zero; panel parent not set.");
    }
  }

  private void OnFormClosing(object? sender, FormClosingEventArgs e)
  {
    LogPanelInfo($"Panel closing event received. CloseReason={e.CloseReason}.");

    if (e.CloseReason == CloseReason.UserClosing)
    {
      e.Cancel = true;
      Hide();
      LogPanelInfo("User close intercepted; panel hidden instead of disposed.");
    }
  }

  private static ServiceProvider ServiceContainer => s_container ??= BuildContainer();

  private static ServiceProvider BuildContainer()
  {
    LogPanelInfo("Building MicroStation service container.");

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

    LogPanelInfo("MicroStation service container ready.");
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
    EnsureBootstrapLogInitialized();
    LogPanelInfo("CreateOrFocus invoked.");

    try
    {
      if (s_instance is { IsDisposed: false })
      {
        LogPanelInfo("Reusing existing panel instance.");

        if (!s_instance.Visible)
        {
          s_instance.Show();
          LogPanelInfo("Existing panel was hidden and is now shown.");
        }

        s_instance.WindowState = FormWindowState.Normal;
        s_instance.BringToFront();
        s_instance.Focus();
        s_instance.Activate();
        LogPanelInfo("Existing panel focused and activated.");
        return;
      }

      LogPanelInfo("Creating new panel instance.");
      s_instance = new SpeckleMicroStationPanel();
      s_instance.Show();
      s_instance.Activate();
      LogPanelInfo("New panel shown and activated.");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ReportPanelError("Failed to create or focus Speckle panel.", ex);
      throw;
    }
  }

  protected override void OnFormClosed(FormClosedEventArgs e)
  {
    base.OnFormClosed(e);
    s_instance = null;
    LogPanelInfo("Panel form closed and instance cleared.");
  }

  private static void EnsureBootstrapLogInitialized()
  {
    if (s_bootstrapLogInitialized)
    {
      return;
    }

    LogPanelInfo("MicroStation panel bootstrap initialized.");
    s_bootstrapLogInitialized = true;
  }

  private static HostAppVersion GetVersion() =>
#if MICROSTATION2026 || OPENROADS2026 || OPENRAIL2026
    HostAppVersion.v2026;
#else
    throw new NotSupportedException("Unsupported Bentley host version.");
#endif

  internal static void ReportPanelError(string message, Exception? ex = null) => LogPanelError(message, ex);

  private static void WriteBootstrapLog(string level, string message, Exception? ex)
  {
    try
    {
      var details = new StringBuilder(message);
      if (ex is not null)
      {
        details.AppendLine().Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
          details.AppendLine().Append(ex.StackTrace);
        }
      }

      string logLine = $"[{DateTime.UtcNow:O}] [{level}] {details}{Environment.NewLine}";
      string directory = Path.GetDirectoryName(s_bootstrapLogPath) ?? string.Empty;
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }

      File.AppendAllText(s_bootstrapLogPath, logLine);
      System.Diagnostics.Debug.WriteLine(logLine);
    }
    catch (IOException)
    {
      // never throw from bootstrap logging
    }
    catch (UnauthorizedAccessException)
    {
      // never throw from bootstrap logging
    }
    catch (ArgumentException)
    {
      // never throw from bootstrap logging
    }
    catch (NotSupportedException)
    {
      // never throw from bootstrap logging
    }
    catch (InvalidOperationException)
    {
      // never throw from bootstrap logging
    }
  }

  private static string BuildBootstrapLogPath()
  {
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    string folder = Path.Combine(appData, "Speckle", "Logs", "MicroStation");

    try
    {
      Directory.CreateDirectory(folder);
    }
    catch (IOException)
    {
      // never throw from path initialization
    }
    catch (UnauthorizedAccessException)
    {
      // never throw from path initialization
    }
    catch (ArgumentException)
    {
      // never throw from path initialization
    }
    catch (NotSupportedException)
    {
      // never throw from path initialization
    }
    catch (InvalidOperationException)
    {
      // never throw from path initialization
    }

    return Path.Combine(folder, "SpeckleBootstrap.log");
  }
}
