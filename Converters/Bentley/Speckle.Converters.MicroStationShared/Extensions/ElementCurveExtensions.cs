using Bentley.GeometryNET;

namespace Speckle.Converters.MicroStation.Extensions;

public static class ElementCurveExtensions
{
  /// <summary>
  /// Returns the curve vector for any curve-bearing element, or null when the element has no path geometry.
  /// </summary>
  public static BG.CurveVector? GetCurveVectorOrNull(this BDE.Element element)
  {
    var eleTryp = element.ElementType;
    CurveVector? query;
    switch (eleTryp)
    {
      case BDPN.MSElementType.BsplineCurve:
        var bsplineElement = (BDE.BSplineCurveElement)element;
        query = bsplineElement.GetCurveVector();
        break;
      case BDPN.MSElementType.Arc:
        var arcElement = (BDE.ArcElement)element;
        query = arcElement.GetCurveVector();
        break;
      case BDPN.MSElementType.Line:
        var lineElement = (BDE.LineElement)element;
        query = lineElement.GetCurveVector();
        break;
      case BDPN.MSElementType.Curve:
        var curveElement = (BDE.CurveElement)element;
        query = curveElement.GetCurveVector();
        break;
      case BDPN.MSElementType.Ellipse:
        var ellipseElement = (BDE.EllipseElement)element;
        query = ellipseElement.GetCurveVector();
        break;
      case BDPN.MSElementType.LineString:
        var lineStringElement = (BDE.LineStringElement)element;
        query = lineStringElement.GetCurveVector();
        break;
      default:
        return null;
    }
    //var query = 
    //BDPN.CurvePathQuery.GetAsCurvePathQuery(element);

    return query;
  }
}
