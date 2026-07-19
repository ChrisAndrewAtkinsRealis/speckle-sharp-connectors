using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

public class DSegment3dToSpeckleRawConverter(
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : ITypedConverter<BG.DSegment3d, SOG.Line>
{
  public SOG.Line Convert(BG.DSegment3d target)
  {
    double length = target.Length / settingsStore.Current.UorPerMaster;
    return new()
    {
      start = pointConverter.Convert(target.StartPoint),
      end = pointConverter.Convert(target.EndPoint),
      domain = new SOP.Interval { start = 0, end = length },
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
