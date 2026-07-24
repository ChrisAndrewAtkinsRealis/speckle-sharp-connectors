using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.MicroStation.HostApp;

namespace Speckle.Connectors.MicroStation.Bindings;

/// <summary>
/// Reads the current MicroStation selection set on demand.
/// NOTE: the managed API does not expose a reliable selection-changed event, so the selection is
/// read when the UI asks for it rather than pushed on change.
/// </summary>
public class MicroStationSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public MicroStationSelectionBinding(IBrowserBridge parent)
  {
    Parent = parent;
  }

  public SelectionInfo GetSelection()
  {
    var objectIds = new List<string>();
    var typeNames = new HashSet<string>();

    uint numSelected = BDPN.SelectionSetManager.NumSelected();
    var modelRef = BMPN.Session.Instance.GetActiveDgnModelRef();

    for (uint i = 0; i < numSelected; i++)
    {
      BDE.Element? element = null;
      BDPN.SelectionSetManager.GetElement(i, ref element, ref modelRef);
      if (element is null)
      {
        continue;
      }

      // reference elements carry their own model ref (a DgnAttachment); encode them so they can be told
      // apart from active-model elements and resolved back through the attachment on send.
      string id = modelRef is BDPN.DgnAttachment attachment
        ? MicroStationReferenceService.EncodeReferenceId(attachment, element)
        : element.ElementId.ToString();

      objectIds.Add(id);
      typeNames.Add(element.GetType().Name);
    }

    return new SelectionInfo(objectIds, $"{objectIds.Count} objects ({string.Join(", ", typeNames)})");
  }
}
