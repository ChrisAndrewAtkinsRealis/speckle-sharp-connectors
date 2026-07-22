using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Circle, BDE.EllipseElement>
{
  public object Convert(Base target) => Convert((SOG.Circle)target);

  public BDE.EllipseElement Convert(SOG.Circle target)
  {
    var center = scaler.PointToNative(target.plane.origin);
    var normal = new BG.DVector3d(target.plane.normal.x, target.plane.normal.y, target.plane.normal.z);
    double radius = scaler.ScaleToNative(target.radius, target.units);

    var ellipse = BG.DEllipse3d.FromCenterRadiusNormal(center, radius, normal);
    return new BDE.EllipseElement(settingsStore.Current.Model, null, ellipse);
  }
}
