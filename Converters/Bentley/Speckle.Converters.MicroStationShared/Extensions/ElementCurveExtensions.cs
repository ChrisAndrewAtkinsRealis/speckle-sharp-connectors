using Bentley.GeometryNET;

namespace Speckle.Converters.MicroStation.Extensions;

public static class ElementCurveExtensions
{
  /// <summary>
  /// Returns the curve vector for any curve-bearing element, or null when the element has no path geometry.
  /// </summary>
  public static BG.CurveVector? GetCurveVectorOrNull(this BDE.Element element)
  {
    var elementType = element.ElementType;
    CurveVector? query;
    switch (elementType)
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
      case BDPN.MSElementType.ComplexString:
        var complexStringElement = (BDE.ComplexStringElement)element;
        query = complexStringElement.GetCurveVector();
        break;
      case BDPN.MSElementType.ComplexShape:
        var complexShapeElement = (BDE.ComplexShapeElement)element;
        query = complexShapeElement.GetCurveVector();
        break;
      case BDPN.MSElementType.Shape:
        var shapeElement = (BDE.ShapeElement)element;
        query = shapeElement.GetCurveVector();
        break;
      default:
        return null;
    }

    return query;
  }
}
