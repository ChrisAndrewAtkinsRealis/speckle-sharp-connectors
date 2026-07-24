namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Keyin handlers wired up in commands.xml. The <c>Speckle</c> keyin opens the connector panel.
/// </summary>
public static class Keyins
{
  public static void Start(string unparsed)
  {
    _ = unparsed;
    SpeckleMicroStationPanel.CreateOrFocus();
  }
}
