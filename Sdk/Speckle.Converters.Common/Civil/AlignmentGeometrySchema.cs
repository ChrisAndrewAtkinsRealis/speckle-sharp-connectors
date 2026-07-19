namespace Speckle.Converters.Common.Civil;

/// <summary>
/// Connector-neutral keys for an alignment's <b>structured horizontal geometry</b> — the parametric segments
/// (lines, circular arcs, spirals) needed to <em>faithfully rebuild</em> an alignment, as opposed to its
/// display/stroked curves which cannot round-trip.
/// </summary>
/// <remarks>
/// <para>
/// This exists because rebuilding an alignment from stroked geometry loses the true arcs and spirals (a known
/// pitfall). An alignment DataObject therefore carries a <c>horizontalGeometry</c> list of segments; each
/// segment is a dictionary keyed by <see cref="SEGMENT_TYPE"/> plus the parameters that segment type needs:
/// </para>
/// <code>
/// "horizontalGeometry": [
///   { "segmentType": "line",   "start": [x,y,z], "end": [x,y,z] },
///   { "segmentType": "arc",    "start": [x,y,z], "end": [x,y,z], "through": [x,y,z] },
///   { "segmentType": "spiral", "start": [x,y,z], "startRadius": 0, "endRadius": 500,
///                              "length": 60, "startAngle": 1.57, "direction": "cw" }
/// ]
/// </code>
/// <para>
/// Points are in the alignment's units. On receive these map straight onto the CifNET constructors
/// <c>Line.Create</c>, <c>CircularArc.Create3(start, end, through)</c> and
/// <c>Spiral.Create1(start, rStart, rEnd, length, angle, SpiralType, Hand)</c>.
/// </para>
/// </remarks>
public static class AlignmentGeometrySchema
{
  public const string HORIZONTAL_GEOMETRY = "horizontalGeometry";

  public const string SEGMENT_TYPE = "segmentType";
  public const string LINE = "line";
  public const string ARC = "arc";
  public const string SPIRAL = "spiral";

  public const string START = "start";
  public const string END = "end";
  public const string THROUGH = "through"; // arc: a point on the arc between start and end

  public const string START_RADIUS = "startRadius"; // spiral
  public const string END_RADIUS = "endRadius"; // spiral
  public const string LENGTH = "length"; // spiral
  public const string START_ANGLE = "startAngle"; // spiral (radians)
  public const string DIRECTION = "direction"; // "cw" | "ccw"
}
