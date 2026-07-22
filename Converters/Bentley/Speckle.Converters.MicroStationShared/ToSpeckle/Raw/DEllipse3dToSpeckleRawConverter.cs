using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native ellipse/arc primitive to the appropriate Speckle curve:
/// a <see cref="SOG.Circle"/> for full circular sweeps, a <see cref="SOG.Arc"/> for circular arcs,
/// and a nurbs <see cref="SOG.Curve"/> for elliptic arcs (mirroring the v2 converter behaviour).
/// </summary>
public class DEllipse3dToSpeckleRawConverter(
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  ITypedConverter<BG.DPlane3d, SOG.Plane> planeConverter,
  ITypedConverter<BG.MSBsplineCurve, SOG.Curve> bsplineConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : ITypedConverter<BG.DEllipse3d, ICurve>
{
  public ICurve Convert(BG.DEllipse3d target)
  {
    string units = settingsStore.Current.SpeckleUnits;
    double uor = settingsStore.Current.UorPerMaster;

    if (target.IsCircular(out double radius, out BG.DVector3d normal))
    {
      var startPoint = target.PointAtAngle(target.StartAngle);
      var endPoint = target.PointAtAngle(target.EndAngle);
      double sweep = target.SweepAngle.Radians;

      if (Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-9 || startPoint.IsAlmostEqualTo(endPoint, 1e-9))
      {
        // full circle
        return new SOG.Circle
        {
          plane = planeConverter.Convert(new BG.DPlane3d(target.Center, normal)),
          radius = radius / uor,
          units = units,
        };
      }

      // circular arc: flip the plane normal for negative sweeps so arc direction is preserved
      var plane = new BG.DPlane3d(target.Center, normal);
      if (sweep < 0)
      {
        plane.NegateNormalInPlace();
      }

      var midPoint = target.PointAtAngle(target.StartAngle + BG.Angle.Multiply(target.SweepAngle, 0.5));
      double length = target.ArcLength() / uor;

      return new SOG.Arc
      {
        startPoint = pointConverter.Convert(startPoint),
        midPoint = pointConverter.Convert(midPoint),
        endPoint = pointConverter.Convert(endPoint),
        plane = planeConverter.Convert(plane),
        domain = new SOP.Interval { start = 0, end = length },
        units = units,
      };
    }

    // elliptic arc: convert via a proxy b-spline, like the v2 converter did
    var ellipse = target;
    var spline = BG.MSBsplineCurve.CreateFromDEllipse3d(ref ellipse);
    return bsplineConverter.Convert(spline);
  }
}
