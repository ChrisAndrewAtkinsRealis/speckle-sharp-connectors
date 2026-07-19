namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Thin wrapper over the MicroStation session for the pieces of active-document state the connector needs.
/// </summary>
public class MicroStationContext
{
  public BDPN.DgnFile? ActiveFile => BMPN.Session.Instance?.GetActiveDgnFile();

  public BDPN.DgnModel? ActiveModel => BMPN.Session.Instance?.GetActiveDgnModel();

  public string? ActiveFileName => ActiveFile?.GetFileName();

  /// <summary>
  /// Finds an element in the active model by its speckle application id (the element id).
  /// </summary>
  public BDE.Element? FindElement(string applicationId)
  {
    var model = ActiveModel;
    if (model is null || !ulong.TryParse(applicationId, out ulong elementId))
    {
      return null;
    }

    return model.FindElementById((BDPN.ElementId)elementId);
  }
}
