using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Points are baked as zero-length line elements, matching the v2 connector behaviour.
/// </summary>
[NameAndRankValue(typeof(SOG.Point), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Point, BDE.LineElement>
{
  public object Convert(Base target) => Convert((SOG.Point)target);

  public BDE.LineElement Convert(SOG.Point target)
  {
    var point = scaler.PointToNative(target);
    var segment = new BG.DSegment3d(point, point);
    return new BDE.LineElement(settingsStore.Current.Model, null, segment);
  }
}
