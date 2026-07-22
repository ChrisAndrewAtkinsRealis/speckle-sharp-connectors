using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native vector to a Speckle vector. Vectors are directions: they are not scaled by UoRs.
/// </summary>
public class DVector3dToSpeckleRawConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : ITypedConverter<BG.DVector3d, SOG.Vector>
{
  public SOG.Vector Convert(BG.DVector3d target) =>
    new(target.X, target.Y, target.Z, settingsStore.Current.SpeckleUnits);
}
