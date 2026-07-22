using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native b-spline curve to a Speckle nurbs curve, including a stroked display polyline.
/// Ported from the v2 ConverterBentley b-spline conversion.
/// </summary>
public class MSBsplineCurveToSpeckleRawConverter(
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : ITypedConverter<BG.MSBsplineCurve, SOG.Curve>
{
  private const int DISPLAY_VALUE_SEGMENT_COUNT = 100;

  public SOG.Curve Convert(BG.MSBsplineCurve target)
  {
    double uor = settingsStore.Current.UorPerMaster;
    string units = settingsStore.Current.SpeckleUnits;

    bool closed = target.IsClosed;
    double length = target.Length() / uor;

    List<BG.DPoint3d> poles = target.Poles.ToList();
    if (closed && poles.Count > 0)
    {
      poles.Add(poles[0]);
    }

    List<double> knots = target.Knots?.ToList() ?? new List<double>();
    List<double> weights = target.Weights?.ToList() ?? Enumerable.Repeat(1.0, poles.Count).ToList();

    // stroke the curve for the display polyline
    var displayPoints = new List<double>(3 * (DISPLAY_VALUE_SEGMENT_COUNT + 1));
    for (int i = 0; i <= DISPLAY_VALUE_SEGMENT_COUNT; i++)
    {
      target.FractionToPoint(out BG.DPoint3d point, (double)i / DISPLAY_VALUE_SEGMENT_COUNT);
      displayPoints.Add(point.X / uor);
      displayPoints.Add(point.Y / uor);
      displayPoints.Add(point.Z / uor);
    }

    var displayValue = new SOG.Polyline
    {
      value = displayPoints,
      closed = closed,
      units = units,
    };

    var controlPoints = new List<double>(3 * poles.Count);
    foreach (var pole in poles)
    {
      var point = pointConverter.Convert(pole);
      controlPoints.Add(point.x);
      controlPoints.Add(point.y);
      controlPoints.Add(point.z);
    }

    return new SOG.Curve
    {
      degree = target.Order - 1,
      periodic = false,
      rational = target.IsRational,
      closed = closed,
      points = controlPoints,
      knots = knots,
      weights = weights,
      length = length,
      domain = new SOP.Interval { start = 0, end = length },
      displayValue = displayValue,
      units = units,
    };
  }
}
