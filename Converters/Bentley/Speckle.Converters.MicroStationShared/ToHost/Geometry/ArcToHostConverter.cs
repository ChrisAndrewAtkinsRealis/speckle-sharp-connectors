using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Arc, BDE.ArcElement>
{
  public object Convert(Base target) => Convert((SOG.Arc)target);

  public BDE.ArcElement Convert(SOG.Arc target)
  {
    if (
      !BG.DEllipse3d.TryCircularArcFromStartMiddleEnd(
        scaler.PointToNative(target.startPoint),
        scaler.PointToNative(target.midPoint),
        scaler.PointToNative(target.endPoint),
        out BG.DEllipse3d ellipse
      )
    )
    {
      throw new ConversionException("Could not construct a circular arc from the arc's start/mid/end points.");
    }

    return new BDE.ArcElement(settingsStore.Current.Model, null, ellipse);
  }
}
