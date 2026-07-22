using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Curve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CurveToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Curve, BDE.BSplineCurveElement>
{
  public object Convert(Base target) => Convert((SOG.Curve)target);

  public BDE.BSplineCurveElement Convert(SOG.Curve target)
  {
    var points = scaler.PointListToNative(target.points, target.units);

    // uniform weights mean a non-rational curve; the native api expects null weights in that case
    List<double>? weights = target.weights.Distinct().Count() == 1 ? null : target.weights;

    var spline = BG.MSBsplineCurve.CreateFromPoles(
      points,
      weights,
      target.knots,
      target.degree + 1,
      target.closed,
      false
    );

    if (target.closed)
    {
      spline.MakeClosed();
    }

    return new BDE.BSplineCurveElement(settingsStore.Current.Model, null, spline);
  }
}
