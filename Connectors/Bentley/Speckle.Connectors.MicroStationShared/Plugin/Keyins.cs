using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Keyin handlers wired up in commands.xml. The <c>Speckle</c> keyin opens the connector panel.
/// </summary>
public static class Keyins
{
  public static void Start(string unparsed)
  {
    _ = unparsed;
    SpeckleMicroStationPanel.LogPanelInfo("Keyin Start invoked.");

    try
    {
      SpeckleMicroStationPanel.CreateOrFocus();
      SpeckleMicroStationPanel.LogPanelInfo("Keyin Start completed.");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      SpeckleMicroStationPanel.ReportPanelError("Failed to open Speckle panel from keyin.", ex);
    }
  }
}
