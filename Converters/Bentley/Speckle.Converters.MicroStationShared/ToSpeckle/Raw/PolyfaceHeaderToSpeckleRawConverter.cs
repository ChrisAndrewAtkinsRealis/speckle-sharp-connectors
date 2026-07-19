using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Converts native polyface mesh data to a Speckle mesh.
/// MicroStation stores faces as 1-based point indices with 0 acting as the face loop terminator/pad.
/// </summary>
public class PolyfaceHeaderToSpeckleRawConverter(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : ITypedConverter<BG.PolyfaceHeader, SOG.Mesh>
{
  public SOG.Mesh Convert(BG.PolyfaceHeader target)
  {
    double uor = settingsStore.Current.UorPerMaster;

    var vertices = new List<double>();
    foreach (var point in target.Point)
    {
      vertices.Add(point.X / uor);
      vertices.Add(point.Y / uor);
      vertices.Add(point.Z / uor);
    }

    var faces = new List<int>();
    var faceIndices = new List<int>();
    foreach (int pointIndex in target.PointIndex)
    {
      if (pointIndex != 0)
      {
        // indices can be negative to flag hidden edges; magnitude is the 1-based vertex index
        faceIndices.Add(Math.Abs(pointIndex) - 1);
      }
      else
      {
        if (faceIndices.Count >= 3)
        {
          faces.Add(faceIndices.Count);
          faces.AddRange(faceIndices);
        }
        faceIndices.Clear();
      }
    }

    // handle a trailing face loop without terminator
    if (faceIndices.Count >= 3)
    {
      faces.Add(faceIndices.Count);
      faces.AddRange(faceIndices);
    }

    return new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
