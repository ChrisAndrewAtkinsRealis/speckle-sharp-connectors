namespace Speckle.Converters.MicroStation.Extensions;

public static class ElementCurveExtensions
{
  /// <summary>
  /// Returns the curve vector for any curve-bearing element, or null when the element has no path geometry.
  /// </summary>
  public static BG.CurveVector? GetCurveVectorOrNull(this BDE.Element element)
  {
    var query = BDPN.CurvePathQuery.GetAsCurvePathQuery(element);
    return query?.GetCurveVector();
  }
}
