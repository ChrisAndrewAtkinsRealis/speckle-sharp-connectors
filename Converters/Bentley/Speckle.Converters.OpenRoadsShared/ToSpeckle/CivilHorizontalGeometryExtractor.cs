using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Civil;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;

namespace Speckle.Converters.OpenRoads.ToSpeckle;

/// <summary>
/// Extracts an alignment's <b>structured</b> horizontal geometry (lines / circular arcs / spirals with their
/// defining parameters) into the connector-neutral <see cref="AlignmentGeometrySchema"/> shape, so the
/// alignment can be faithfully rebuilt (via <c>CircularArc.Create3</c> / <c>Spiral.Create1</c>) rather than
/// approximated from display curves.
/// </summary>
/// <remarks>
/// ⚠ This read is the fragile part (per hard-won experience): getting the true parametric geometry out of the
/// CifNET <c>LinearGeometry</c> — especially spiral parameters and arc through-points — is version-sensitive
/// and easy to get subtly wrong, which is what breaks rebuild. It is therefore fully defensive: any element
/// it can't read is skipped, and the caller still attaches the display curves as a visualization fallback.
/// Every CifNET member access below is a live-SDK surface to validate.
/// </remarks>
public class CivilHorizontalGeometryExtractor(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  ILogger<CivilHorizontalGeometryExtractor> logger
)
{
  public List<Dictionary<string, object?>> Extract(CifGMSDK.Alignment alignment)
  {
    var segments = new List<Dictionary<string, object?>>();

    try
    {
      foreach (var element in GetSubElements(alignment.LinearGeometry))
      {
        var segment = ExtractSegment(element);
        if (segment is not null)
        {
          segments.Add(segment);
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(
        ex,
        "Failed to extract structured horizontal geometry; rebuild will fall back to display curves"
      );
      segments.Clear();
    }

    return segments;
  }

  private static IEnumerable<CifLG.LinearElement> GetSubElements(CifLG.LinearElement geometry)
  {
    // a complex alignment is a LinearComplex of sub-elements; a simple one is a single element
    if (geometry is CifLG.LinearComplex complex && complex.Elements is { } elements)
    {
      return elements;
    }

    return new[] { geometry };
  }

  private Dictionary<string, object?>? ExtractSegment(CifLG.LinearElement element)
  {
    try
    {
      switch (element)
      {
        case CifLG.Line line:
          return new Dictionary<string, object?>
          {
            [AlignmentGeometrySchema.SEGMENT_TYPE] = AlignmentGeometrySchema.LINE,
            [AlignmentGeometrySchema.START] = ToMaster(line.StartPoint),
            [AlignmentGeometrySchema.END] = ToMaster(line.EndPoint),
          };

        case CifLG.CircularArc arc:
          return new Dictionary<string, object?>
          {
            [AlignmentGeometrySchema.SEGMENT_TYPE] = AlignmentGeometrySchema.ARC,
            [AlignmentGeometrySchema.START] = ToMaster(arc.StartPoint),
            [AlignmentGeometrySchema.END] = ToMaster(arc.EndPoint),
            [AlignmentGeometrySchema.THROUGH] = ToMaster(arc.PointAtDistance(arc.Length / 2.0)),
          };

        case CifLG.Spiral spiral:
          return new Dictionary<string, object?>
          {
            [AlignmentGeometrySchema.SEGMENT_TYPE] = AlignmentGeometrySchema.SPIRAL,
            [AlignmentGeometrySchema.START] = ToMaster(spiral.StartPoint),
            [AlignmentGeometrySchema.START_RADIUS] = spiral.RadiusStart,
            [AlignmentGeometrySchema.END_RADIUS] = spiral.RadiusEnd,
            [AlignmentGeometrySchema.LENGTH] = spiral.Length,
            [AlignmentGeometrySchema.START_ANGLE] = spiral.StartAngle,
            [AlignmentGeometrySchema.DIRECTION] = spiral.IsClockwise ? "cw" : "ccw",
          };

        default:
          return null; // unknown element type - display fallback covers it
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Skipped an unreadable horizontal geometry element");
      return null;
    }
  }

  private double[] ToMaster(BG.DPoint3d point)
  {
    double uor = settingsStore.Current.UorPerMaster;
    return new[] { point.X / uor, point.Y / uor, point.Z / uor };
  }
}
