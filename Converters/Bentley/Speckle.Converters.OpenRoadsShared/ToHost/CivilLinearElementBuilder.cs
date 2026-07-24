using System.Reflection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Civil;
using Speckle.Converters.MicroStation;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.ToHost;

/// <summary>
/// Builds a native CifNET <see cref="CifLG.LinearComplex"/> (the input to alignment/profile creation) from a
/// Speckle civil object. Prefers the structured <see cref="AlignmentGeometrySchema"/> horizontal geometry
/// (true lines/arcs/spirals, so the alignment rebuilds faithfully) and falls back to the display curves as a
/// straight-segment approximation only when structured geometry is absent.
/// </summary>
/// <remarks>
/// The CifNET constructors (<c>Line.Create</c>, <c>CircularArc.Create3</c>, <c>Spiral.Create1</c>,
/// <c>LinearComplex.Create1</c>) match the ATRL/Atom ORD usage but remain a live-SDK surface.
/// </remarks>
public class CivilLinearElementBuilder(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
)
{
  /// <summary>Builds from the DataObject, preferring structured geometry over display curves.</summary>
  public CifLG.LinearComplex? Build(DataObject civilObject)
  {
    if (
      civilObject.properties.TryGetValue(AlignmentGeometrySchema.HORIZONTAL_GEOMETRY, out var raw)
      && raw is IEnumerable<object?> segments
    )
    {
      var complex = BuildFromSegments(segments);
      if (complex is not null)
      {
        return complex;
      }
    }

    return BuildFromDisplay(civilObject.displayValue);
  }

  private CifLG.LinearComplex? BuildFromSegments(IEnumerable<object?> segments)
  {
    var elements = new List<CifLG.LinearElement>();
    foreach (var segmentObj in segments)
    {
      if (segmentObj is not IReadOnlyDictionary<string, object?> segment)
      {
        continue;
      }

      try
      {
        var element = BuildSegment(segment);
        if (element is not null)
        {
          elements.Add(element);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // skip an unbuildable segment; a partial baseline is better than none
      }
    }

    return elements.Count == 0 ? null : CifLG.LinearComplex.Create1(elements.ToArray(), false, false, 0.001);
  }

  private CifLG.LinearElement? BuildSegment(IReadOnlyDictionary<string, object?> segment)
  {
    string? type = segment.TryGetValue(AlignmentGeometrySchema.SEGMENT_TYPE, out var t) ? t as string : null;
    switch (type)
    {
      case AlignmentGeometrySchema.LINE:
        return CreateLinearGeometryElement<CifLG.LinearElement>(
          typeof(CifLG.Line),
          "Create",
          Point(segment, AlignmentGeometrySchema.START),
          Point(segment, AlignmentGeometrySchema.END)
        );

      case AlignmentGeometrySchema.ARC:
        return CreateLinearGeometryElement<CifLG.LinearElement>(
          typeof(CifLG.CircularArc),
          "Create3",
          Point(segment, AlignmentGeometrySchema.START),
          Point(segment, AlignmentGeometrySchema.END),
          Point(segment, AlignmentGeometrySchema.THROUGH)
        );

      case AlignmentGeometrySchema.SPIRAL:
        var hand =
          (segment.TryGetValue(AlignmentGeometrySchema.DIRECTION, out var d) ? d as string : null) == "ccw"
            ? CifLG.Hand.CounterClockwise
            : CifLG.Hand.Clockwise;
        return CreateLinearGeometryElement<CifLG.LinearElement>(
          typeof(CifLG.Spiral),
          "Create1",
          Point(segment, AlignmentGeometrySchema.START),
          Double(segment, AlignmentGeometrySchema.START_RADIUS),
          Double(segment, AlignmentGeometrySchema.END_RADIUS),
          Double(segment, AlignmentGeometrySchema.LENGTH),
          Double(segment, AlignmentGeometrySchema.START_ANGLE),
          CifLG.SpiralType.Clothoid,
          hand
        );

      default:
        return null;
    }
  }

  private static TLinearElement CreateLinearGeometryElement<TLinearElement>(Type geometryType, string methodName, params object?[] arguments)
    where TLinearElement : class
  {
    foreach (var method in geometryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
      if (method.Name != methodName || method.GetParameters().Length != arguments.Length)
      {
        continue;
      }

      if (!method.ReturnType.IsAssignableTo(typeof(TLinearElement)))
      {
        continue;
      }

      return (TLinearElement)method.Invoke(null, arguments)!;
    }

    throw new MissingMethodException(geometryType.FullName, methodName);
  }

  private BG.DPoint3d Point(IReadOnlyDictionary<string, object?> segment, string key)
  {
    if (segment.TryGetValue(key, out var raw) && raw is IEnumerable<object?> coords)
    {
      var values = coords.Select(static c => Convert.ToDouble(c)).ToArray();
      if (values.Length >= 3)
      {
        double factor = settingsStore.Current.UorPerMaster;
        return new BG.DPoint3d(values[0] * factor, values[1] * factor, values[2] * factor);
      }
    }

    return new BG.DPoint3d(0, 0, 0);
  }

  private static double Double(IReadOnlyDictionary<string, object?> segment, string key) =>
    segment.TryGetValue(key, out var raw) && raw is not null ? Convert.ToDouble(raw) : 0;

  // ---- display-curve fallback (straight segments through the display points) ----

  private CifLG.LinearComplex? BuildFromDisplay(IReadOnlyList<Base> displayValue)
  {
    var points = ExtractPoints(displayValue);
    if (points.Count < 2)
    {
      return null;
    }

    var elements = new List<CifLG.LinearElement>(points.Count - 1);
    for (int i = 0; i < points.Count - 1; i++)
    {
      if (!points[i].IsAlmostEqual(points[i + 1]))
      {
        elements.Add(CreateLinearGeometryElement<CifLG.LinearElement>(typeof(CifLG.Line), "Create", points[i], points[i + 1]));
      }
    }

    return elements.Count == 0
      ? null
      : CreateLinearGeometryElement<CifLG.LinearComplex>(
        typeof(CifLG.LinearComplex),
        "Create1",
        elements.ToArray(),
        false,
        false,
        0.001
      );
  }

  private List<BG.DPoint3d> ExtractPoints(IReadOnlyList<Base> displayValue)
  {
    var points = new List<BG.DPoint3d>();
    foreach (var item in displayValue)
    {
      try
      {
        AddCurvePoints(item, points);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // skip an uninterpretable display item
      }
    }

    return Dedupe(points);
  }

  private void AddCurvePoints(Base item, List<BG.DPoint3d> points)
  {
    switch (item)
    {
      case SOG.Line line:
        points.Add(scaler.PointToNative(line.start));
        points.Add(scaler.PointToNative(line.end));
        break;
      case SOG.Polyline polyline:
        AddFlatCoordinates(polyline.value, polyline.units, points);
        break;
      case SOG.Arc arc:
        points.Add(scaler.PointToNative(arc.startPoint));
        points.Add(scaler.PointToNative(arc.midPoint));
        points.Add(scaler.PointToNative(arc.endPoint));
        break;
      case Speckle.Objects.Geometry.Curve curve when curve.displayValue is SOG.Polyline poly:
        AddFlatCoordinates(poly.value, poly.units, points);
        break;
      case IDisplayValue<IReadOnlyList<Base>> hasDisplay:
        foreach (var d in hasDisplay.displayValue)
        {
          AddCurvePoints(d, points);
        }
        break;
    }
  }

  private void AddFlatCoordinates(IReadOnlyList<double> coordinates, string units, List<BG.DPoint3d> points)
  {
    for (int i = 0; i + 2 < coordinates.Count; i += 3)
    {
      points.Add(
        new BG.DPoint3d(
          scaler.ScaleToNative(coordinates[i], units),
          scaler.ScaleToNative(coordinates[i + 1], units),
          scaler.ScaleToNative(coordinates[i + 2], units)
        )
      );
    }
  }

  private static List<BG.DPoint3d> Dedupe(List<BG.DPoint3d> points)
  {
    var result = new List<BG.DPoint3d>(points.Count);
    foreach (var p in points)
    {
      if (result.Count == 0 || !result[^1].IsAlmostEqual(p))
      {
        result.Add(p);
      }
    }

    return result;
  }
}

internal static class DPoint3dCivilExtensions
{
  public static bool IsAlmostEqual(this BG.DPoint3d a, BG.DPoint3d b)
  {
    double dx = a.X - b.X;
    double dy = a.Y - b.Y;
    double dz = a.Z - b.Z;
    return dx * dx + dy * dy + dz * dz <= 1e-12;
  }
}

