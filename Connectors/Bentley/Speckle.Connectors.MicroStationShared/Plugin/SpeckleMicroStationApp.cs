using System.IO;
using System.Reflection;
using Bentley.MstnPlatformNET;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// MicroStation add-in entry point. Loaded when the DGN app is registered (see .cfg + MS_DGNAPPS).
/// The actual UI is created lazily when the user runs the <c>Speckle</c> keyin.
/// </summary>
[AddIn(MdlTaskID = "Speckle")]
public sealed class SpeckleMicroStationApp : AddIn
{
  private static SpeckleMicroStationApp? s_instance;

  public SpeckleMicroStationApp(IntPtr mdlDesc)
    : base(mdlDesc) { }

  public static SpeckleMicroStationApp? Instance => s_instance;

  protected override int Run(string[] commandLine)
  {
    s_instance = this;

    // Speckle and its dependencies are deployed next to this assembly; help the CLR find them.
    AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

    return 0;
  }

  private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
  {
    string name = new AssemblyName(args.Name).Name + ".dll";
    string? directory = Path.GetDirectoryName(typeof(SpeckleMicroStationApp).Assembly.Location);
    if (directory is null)
    {
      return null;
    }

    string candidate = Path.Combine(directory, name);
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
  }
}
