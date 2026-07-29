namespace Speckle.Converters.MicroStation;

/// <summary>
/// Recenters absolute native coordinates (in model master units) around a per-operation reference origin, so
/// exported geometry stays close to zero instead of carrying real-world/civil-scale magnitudes that lose precision
/// in downstream float32 viewers (e.g. a georeferenced DGN whose design coordinates are true state-plane values).
/// </summary>
public interface IReferencePointConverter
{
  /// <summary>
  /// Converts a point in native master-unit coordinates to coordinates relative to this operation's reference
  /// origin. If no origin has been established yet, this point becomes the origin (and is returned as (0,0,0));
  /// every other point converted in the same scope is returned relative to that same origin.
  /// </summary>
  BG.DPoint3d ConvertToExternalCoordinates(BG.DPoint3d point);

  /// <summary>
  /// Reverses <see cref="ConvertToExternalCoordinates"/>: adds the reference origin back onto a point expressed
  /// relative to it, recovering the original absolute native coordinates. No-op if no origin has been
  /// established/set (<see cref="Origin"/> is <c>null</c>).
  /// </summary>
  BG.DPoint3d ConvertFromExternalCoordinates(BG.DPoint3d point);

  /// <summary>
  /// The reference origin captured for this scope (native master-unit coordinates), or <c>null</c> if none has
  /// been established/set yet.
  /// </summary>
  BG.DPoint3d? Origin { get; }

  /// <summary>
  /// Explicitly sets the reference origin (used on receive, seeded from the value recorded on the root collection
  /// at send time) instead of lazily capturing it from the first converted point.
  /// </summary>
  void SetOrigin(BG.DPoint3d origin);
}
