using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Walks the primitives of a native curve vector and converts each to the corresponding Speckle curve.
/// Used for complex chains/shapes and as a generic curve extractor for other element types.
/// </summary>
public class CurveVectorToSpeckleRawConverter(
  ITypedConverter<BG.DSegment3d, SOG.Line> segmentConverter,
  ITypedConverter<BG.DEllipse3d, ICurve> ellipseConverter,
  ITypedConverter<BG.MSBsplineCurve, SOG.Curve> bsplineConverter,
  ITypedConverter<List<BG.DPoint3d>, SOG.Polyline> polylineConverter
) : ITypedConverter<BG.CurveVector, List<ICurve>>
{
  public List<ICurve> Convert(BG.CurveVector target)
  {
    var segments = new List<ICurve>();

    foreach (var primitive in target)
    {
      var primitiveType = primitive.GetCurvePrimitiveType();
      switch (primitiveType)
      {
        case BG.CurvePrimitive.CurvePrimitiveType.Line:
          if (primitive.TryGetLine(out BG.DSegment3d segment))
          {
            segments.Add(segmentConverter.Convert(segment));
          }
          break;

        case BG.CurvePrimitive.CurvePrimitiveType.Arc:
          if (primitive.TryGetArc(out BG.DEllipse3d arc))
          {
            segments.Add(ellipseConverter.Convert(arc));
          }
          break;

        case BG.CurvePrimitive.CurvePrimitiveType.LineString:
          var points = new List<BG.DPoint3d>();
          if (primitive.TryGetLineString(points))
          {
            segments.Add(polylineConverter.Convert(points));
          }
          break;

        case BG.CurvePrimitive.CurvePrimitiveType.BsplineCurve:
          var spline = primitive.GetBsplineCurve();
          if (spline is not null)
          {
            segments.Add(bsplineConverter.Convert(spline));
          }
          break;

        case BG.CurvePrimitive.CurvePrimitiveType.Spiral:
          // spirals (e.g. transition curves) are approximated with their proxy b-spline
          var proxySpline = primitive.GetProxyBsplineCurve();
          if (proxySpline is not null)
          {
            segments.Add(bsplineConverter.Convert(proxySpline));
          }
          break;

        case BG.CurvePrimitive.CurvePrimitiveType.ChildCurveVector:
          var child = primitive.GetChildCurveVector();
          if (child is not null)
          {
            segments.AddRange(Convert(child));
          }
          break;

        default:
          break;
      }
    }

    return segments;
  }
}
