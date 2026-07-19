namespace Speckle.Converters.MicroStation.Extensions;

/// <summary>
/// Small tolerance helpers implemented from primitive components so we don't depend on
/// Bentley geometry helper methods whose availability varies across SDK versions.
/// </summary>
public static class GeometryExtensions
{
  public static bool IsAlmostEqualTo(this BG.DPoint3d a, BG.DPoint3d b, double tolerance)
  {
    double dx = a.X - b.X;
    double dy = a.Y - b.Y;
    double dz = a.Z - b.Z;
    return dx * dx + dy * dy + dz * dz <= tolerance * tolerance;
  }

  public static bool IsAlmostZero(this BG.DVector3d v, double tolerance) =>
    v.X * v.X + v.Y * v.Y + v.Z * v.Z <= tolerance * tolerance;
}
