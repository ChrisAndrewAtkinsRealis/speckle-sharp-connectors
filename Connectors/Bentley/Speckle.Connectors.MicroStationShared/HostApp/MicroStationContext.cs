namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Thin wrapper over the MicroStation session for the pieces of active-document state the connector needs.
/// Also resolves elements that live in attached references (via <see cref="MicroStationReferenceService"/>).
/// </summary>
public class MicroStationContext
{
  private readonly MicroStationReferenceService _referenceService;

  public MicroStationContext(MicroStationReferenceService referenceService)
  {
    _referenceService = referenceService;
  }

  public BDPN.DgnFile? ActiveFile => BMPN.Session.Instance?.GetActiveDgnFile();

  public BDPN.DgnModel? ActiveModel => BMPN.Session.Instance?.GetActiveDgnModel();

  public string? ActiveFileName => ActiveFile?.GetFileName();

  /// <summary>
  /// Finds an element by its speckle application id. Handles both active-model elements (plain numeric ids)
  /// and reference elements (composite <c>R{attachmentId}:{elementId}</c> ids).
  /// </summary>
  public BDE.Element? FindElement(string applicationId) => FindElementWithModelRef(applicationId).element;

  /// <summary>
  /// Finds an element and the model ref it belongs to. The model ref is the active model for active elements,
  /// or the reference attachment for reference elements (needed to select/highlight in the view).
  /// </summary>
  public (BDE.Element? element, BDPN.DgnModelRef? modelRef) FindElementWithModelRef(string applicationId)
  {
    var model = ActiveModel;
    if (model is null)
    {
      return (null, null);
    }

    if (MicroStationReferenceService.IsReferenceId(applicationId))
    {
      return _referenceService.Resolve(model, applicationId);
    }

    if (!ulong.TryParse(applicationId, out ulong elementId))
    {
      return (null, null);
    }

    return (model.FindElementById((BDPN.ElementId)elementId), BMPN.Session.Instance?.GetActiveDgnModelRef());
  }
}
