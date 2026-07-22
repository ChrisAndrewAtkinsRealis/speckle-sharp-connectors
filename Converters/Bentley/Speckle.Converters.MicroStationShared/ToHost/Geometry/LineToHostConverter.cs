using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Line), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Line, BDE.LineElement>
{
  public object Convert(Base target) => Convert((SOG.Line)target);

  public BDE.LineElement Convert(SOG.Line target)
  {
    var segment = new BG.DSegment3d(scaler.PointToNative(target.start), scaler.PointToNative(target.end));
    return new BDE.LineElement(settingsStore.Current.Model, null, segment);
  }
}
