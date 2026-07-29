using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native point (in UoRs) to a Speckle point (in model master units), recentered around this
/// operation's reference origin (see <see cref="IReferencePointConverter"/>).
/// </summary>
public class DPoint3dToSpeckleRawConverter(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  IReferencePointConverter referencePointConverter
) : ITypedConverter<BG.DPoint3d, SOG.Point>
{
  public SOG.Point Convert(BG.DPoint3d target)
  {
    double uor = settingsStore.Current.UorPerMaster;
    var masterUnitPoint = new BG.DPoint3d(target.X / uor, target.Y / uor, target.Z / uor);
    var extPoint = referencePointConverter.ConvertToExternalCoordinates(masterUnitPoint);
    return new(extPoint.X, extPoint.Y, extPoint.Z, settingsStore.Current.SpeckleUnits);
  }
}
