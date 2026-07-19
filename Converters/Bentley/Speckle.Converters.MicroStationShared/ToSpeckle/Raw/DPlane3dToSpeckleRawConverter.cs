using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

public class DPlane3dToSpeckleRawConverter(
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  ITypedConverter<BG.DVector3d, SOG.Vector> vectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : ITypedConverter<BG.DPlane3d, SOG.Plane>
{
  public SOG.Plane Convert(BG.DPlane3d target)
  {
    BG.DVector3d normal = target.Normal;

    // build an orthonormal frame from the normal, mirroring the v2 converter behaviour
    BG.DVector3d xAxis = BG.DVector3d.UnitY.CrossProduct(normal);
    if (xAxis.IsZeroVector())
    {
      xAxis = BG.DVector3d.UnitX;
    }

    BG.DVector3d yAxis = normal.CrossProduct(xAxis);

    return new()
    {
      origin = pointConverter.Convert(target.Origin),
      normal = vectorConverter.Convert(normal),
      xdir = vectorConverter.Convert(xAxis),
      ydir = vectorConverter.Convert(yAxis),
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
