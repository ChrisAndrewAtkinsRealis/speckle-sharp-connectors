using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native point (in UoRs) to a Speckle point (in model master units).
/// </summary>
public class DPoint3dToSpeckleRawConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : ITypedConverter<BG.DPoint3d, SOG.Point>
{
  public SOG.Point Convert(BG.DPoint3d target)
  {
    double uor = settingsStore.Current.UorPerMaster;
    return new(target.X / uor, target.Y / uor, target.Z / uor, settingsStore.Current.SpeckleUnits);
  }
}
