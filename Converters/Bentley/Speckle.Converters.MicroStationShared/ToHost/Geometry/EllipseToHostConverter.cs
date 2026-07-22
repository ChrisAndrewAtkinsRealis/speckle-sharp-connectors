using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Ellipse), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EllipseToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Ellipse, BDE.EllipseElement>
{
  public object Convert(Base target) => Convert((SOG.Ellipse)target);

  public BDE.EllipseElement Convert(SOG.Ellipse target)
  {
    if (target.firstRadius == 0 || target.secondRadius == 0)
    {
      throw new ValidationException("Ellipse radii cannot be zero.");
    }

    var origin = scaler.PointToNative(target.plane.origin);
    var placement = new BG.DPlacementZX(origin);

    var ellipse = new BG.DEllipse3d(
      placement,
      scaler.ScaleToNative(target.firstRadius, target.units),
      scaler.ScaleToNative(target.secondRadius, target.units),
      BG.Angle.Zero,
      BG.Angle.TWOPI
    );

    return new BDE.EllipseElement(settingsStore.Current.Model, null, ellipse);
  }
}
