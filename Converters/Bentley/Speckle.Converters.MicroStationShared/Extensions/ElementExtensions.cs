namespace Speckle.Converters.MicroStation.Extensions;

public static class ElementExtensions
{
  /// <summary>
  /// The stable id used as the Speckle application id for a MicroStation element.
  /// Element ids are stable per DGN file.
  /// </summary>
  public static string GetSpeckleApplicationId(this BDE.Element element) => element.ElementId.ToString();
}
