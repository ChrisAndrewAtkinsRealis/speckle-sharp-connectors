namespace Speckle.Converters.MicroStation;

/// <summary>
/// Scoped (one instance per send/receive operation, see <c>ServiceRegistration</c>) so every raw converter in the
/// same operation shares one reference origin: whichever point is converted first "wins" and becomes (0,0,0),
/// keeping every other coordinate in the send small relative to it instead of at absolute civil-scale magnitude.
/// </summary>
public class ReferencePointConverter : IReferencePointConverter
{
  private BG.DPoint3d? _origin;

  public BG.DPoint3d? Origin => _origin;

  public void SetOrigin(BG.DPoint3d origin) => _origin = origin;

  public BG.DPoint3d ConvertToExternalCoordinates(BG.DPoint3d point)
  {
    if (_origin is not { } origin)
    {
      _origin = point;
      return new BG.DPoint3d(0, 0, 0);
    }

    return new BG.DPoint3d(point.X - origin.X, point.Y - origin.Y, point.Z - origin.Z);
  }

  public BG.DPoint3d ConvertFromExternalCoordinates(BG.DPoint3d point)
  {
    if (_origin is not { } origin)
    {
      return point;
    }

    return new BG.DPoint3d(point.X + origin.X, point.Y + origin.Y, point.Z + origin.Z);
  }
}
