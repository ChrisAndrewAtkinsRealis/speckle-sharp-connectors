using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts a native point list (in UoRs) to a Speckle polyline (in model master units).
/// </summary>
public class PointListToSpecklePolylineRawConverter(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  IReferencePointConverter referencePointConverter
) : ITypedConverter<List<BG.DPoint3d>, SOG.Polyline>
{
  public SOG.Polyline Convert(List<BG.DPoint3d> target)
  {
    double uor = settingsStore.Current.UorPerMaster;

    bool closed = target.Count > 1 && target[0].IsAlmostEqualTo(target[^1], 1e-9);
    var points = closed ? target.Take(target.Count - 1) : target;

    var coordinates = new List<double>(3 * target.Count);
    foreach (var point in points)
    {
      var extPoint = referencePointConverter.ConvertToExternalCoordinates(
        new BG.DPoint3d(point.X / uor, point.Y / uor, point.Z / uor)
      );
      coordinates.Add(extPoint.X);
      coordinates.Add(extPoint.Y);
      coordinates.Add(extPoint.Z);
    }

    return new SOG.Polyline
    {
      value = coordinates,
      closed = closed,
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
