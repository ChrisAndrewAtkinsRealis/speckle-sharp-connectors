using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Mesh, BDE.MeshHeaderElement>
{
  public object Convert(Base target) => Convert((SOG.Mesh)target);

  public BDE.MeshHeaderElement Convert(SOG.Mesh target)
  {
    var vertices = scaler.PointListToNative(target.vertices, target.units);

    using var meshData = new BG.PolyfaceHeader();

    int j = 0;
    while (j < target.faces.Count)
    {
      int n = target.faces[j];
      if (n < 3)
      {
        n += 3; // 0 -> 3 (triangle), 1 -> 4 (quad): legacy speckle face encoding
      }

      var faceVertices = new List<BG.DPoint3d>(n);
      for (int i = 1; i <= n; i++)
      {
        faceVertices.Add(vertices[target.faces[j + i]]);
      }

      meshData.AddPolygon(faceVertices, new List<BG.DVector3d>(), new List<BG.DPoint2d>());

      j += n + 1;
    }

    return new BDE.MeshHeaderElement(settingsStore.Current.Model, null, meshData);
  }
}
