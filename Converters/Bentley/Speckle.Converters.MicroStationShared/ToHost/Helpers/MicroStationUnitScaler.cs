using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.MicroStation.ToHost.Helpers;

/// <summary>
/// Scales incoming Speckle values (in their declared units) to native UoR coordinates of the active model.
/// </summary>
public class MicroStationUnitScaler(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  IReferencePointConverter referencePointConverter
)
{
  public double ScaleToNative(double value, string? units)
  {
    double factor = units is null ? 1d : Units.GetConversionFactor(units, settingsStore.Current.SpeckleUnits);
    return value * factor * settingsStore.Current.UorPerMaster;
  }

  public BG.DPoint3d PointToNative(SOG.Point point) =>
    ToAbsoluteNative(
      new BG.DPoint3d(
        ScaleToNative(point.x, point.units),
        ScaleToNative(point.y, point.units),
        ScaleToNative(point.z, point.units)
      )
    );

  public BG.DPoint3d[] PointListToNative(IReadOnlyList<double> coordinates, string? units)
  {
    if (coordinates.Count % 3 != 0)
    {
      throw new ValidationException("Point list is malformed: length % 3 != 0.");
    }

    var points = new BG.DPoint3d[coordinates.Count / 3];
    for (int i = 0, k = 0; i < coordinates.Count; i += 3, k++)
    {
      points[k] = ToAbsoluteNative(
        new BG.DPoint3d(
          ScaleToNative(coordinates[i], units),
          ScaleToNative(coordinates[i + 1], units),
          ScaleToNative(coordinates[i + 2], units)
        )
      );
    }

    return points;
  }

  /// <summary>
  /// Adds the reference origin (see <see cref="IReferencePointConverter"/>) back onto an already-scaled native
  /// point, undoing the recentering applied on send so received geometry lands at its true absolute location.
  /// No-op if no origin was restored for this receive (e.g. the sender never recentered anything).
  /// </summary>
  private BG.DPoint3d ToAbsoluteNative(BG.DPoint3d scaled)
  {
    if (referencePointConverter.Origin is not { } origin)
    {
      return scaled;
    }

    // origin is in the receiving document's master units (see MicroStationHostObjectBuilder.RestoreReferencePointOrigin);
    // scaled is already in native UoRs, so bring the origin into UoRs too before adding it back.
    double uor = settingsStore.Current.UorPerMaster;
    return new BG.DPoint3d(scaled.X + origin.X * uor, scaled.Y + origin.Y * uor, scaled.Z + origin.Z * uor);
  }
}
