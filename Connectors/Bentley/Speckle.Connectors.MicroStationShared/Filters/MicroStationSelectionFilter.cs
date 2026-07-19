using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.MicroStation.Filters;

public class MicroStationSelectionFilter : DirectSelectionSendFilter
{
  public MicroStationSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
