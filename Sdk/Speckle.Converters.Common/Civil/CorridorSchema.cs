namespace Speckle.Converters.Common.Civil;

/// <summary>
/// Connector-neutral property keys for a civil <b>corridor</b> sent to Speckle, shared by the OpenRoads/OpenRail
/// and Civil 3D converters so a corridor authored in one can be rebuilt in the other (true ORD&lt;-&gt;C3D
/// interoperability).
/// </summary>
/// <remarks>
/// <para>
/// A corridor is emitted as a <c>DataObject</c> with <c>type = "Corridor"</c>. Its <c>displayValue</c> always
/// carries the corridor's geometry (mesh surfaces / feature lines) so non-civil consumers still see the model.
/// The <c>properties</c> dictionary carries the <em>definition</em> using the keys below, so a civil connector
/// on the receiving side can regenerate the corridor rather than just bake dumb geometry.
/// </para>
/// <para>Intended <c>properties</c> shape (all optional; producers fill what the host exposes):</para>
/// <code>
/// {
///   "baseline": {                     // BASELINE
///     "alignment": { ...DataObject },  //   ALIGNMENT  - horizontal geometry (curves + stationing)
///     "profile":   { ...DataObject }   //   PROFILE    - vertical geometry (PVIs / curves)
///   },
///   "startStation": 0.0,               // START_STATION
///   "endStation": 1234.5,              // END_STATION
///   "keyStations": [ ... ],            // KEY_STATIONS   - doubles
///   "templateDrops": [                 // TEMPLATE_DROPS - typical section applied over a station range
///     { "station": 0.0, "templateName": "2 Lane", "intervalLength": 5.0 }
///   ],
///   "pointControls": [                 // POINT_CONTROLS - horizontal/vertical controls on the template
///     { "name": "EOP", "startStation": 0.0, "endStation": 500.0, "mode": "Horizontal" }
///   ],
///   "superelevation": [                // SUPERELEVATION - cross-slope (and cant, for rail)
///     { "station": 120.0, "leftSlope": -0.02, "rightSlope": 0.06 }
///   ],
///   "surfaces": [ "Top", "Bottom" ]    // SURFACES - corridor surface names
/// }
/// </code>
/// </remarks>
public static class CorridorSchema
{
  /// <summary>The <c>DataObject.type</c> value identifying a corridor.</summary>
  public const string TYPE = "Corridor";

  public const string BASELINE = "baseline";
  public const string ALIGNMENT = "alignment";
  public const string PROFILE = "profile";
  public const string START_STATION = "startStation";
  public const string END_STATION = "endStation";
  public const string KEY_STATIONS = "keyStations";
  public const string TEMPLATE_DROPS = "templateDrops";
  public const string POINT_CONTROLS = "pointControls";
  public const string SUPERELEVATION = "superelevation";
  public const string SURFACES = "surfaces";

  // Nested keys within a template drop entry.
  public static class TemplateDrop
  {
    public const string STATION = "station";
    public const string TEMPLATE_NAME = "templateName";
    public const string INTERVAL_LENGTH = "intervalLength";
  }

  // Nested keys within a point control entry.
  public static class PointControl
  {
    public const string NAME = "name";
    public const string START_STATION = "startStation";
    public const string END_STATION = "endStation";
    public const string MODE = "mode";
  }

  // Nested keys within a superelevation / cant entry.
  public static class Superelevation
  {
    public const string STATION = "station";
    public const string LEFT_SLOPE = "leftSlope";
    public const string RIGHT_SLOPE = "rightSlope";
    public const string CANT = "cant"; // rail
  }
}
