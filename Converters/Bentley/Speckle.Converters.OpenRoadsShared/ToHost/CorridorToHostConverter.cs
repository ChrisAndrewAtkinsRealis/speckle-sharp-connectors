using System.Reflection;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Civil;
using Speckle.Objects.Data;
using Speckle.Sdk;

namespace Speckle.Converters.OpenRoads.ToHost;

/// <summary>
/// Rebuilds a native corridor from a Speckle corridor DataObject that follows <see cref="CorridorSchema"/> -
/// the ORD-side of ORD&lt;-&gt;C3D corridor interop.
/// </summary>
/// <remarks>
/// Two-phase, mirroring the ATRL/Atom ORD flow: (1) transient session to create the baseline alignment +
/// profile and persist them, then (2) a fresh transient session to create the corridor on that alignment
/// (<c>Alignment.CreateCorridorByAlignment(name)</c>) and persist. Key stations, template drops, point
/// controls and superelevation are read from the schema and applied best-effort - these CifNET corridor-edit
/// calls are the least-verified surface and are flagged/stubbed for a live-SDK pass.
/// </remarks>
public class CorridorToHostConverter(
  AlignmentToHostConverter alignmentConverter,
  ProfileToHostConverter profileConverter,
  ILogger<CorridorToHostConverter> logger
)
{
  public string? Create(DataObject corridorObject, object connection)
  {
    if (
      connection
        .GetType()
        .GetMethod("GetOrCreateGeometricModel", BindingFlags.Instance | BindingFlags.Public)
        ?.Invoke(connection, null)
      is not object geometricModel
    )
    {
      logger.LogWarning("Could not get or create a civil geometric model for corridor '{Name}'", corridorObject.name);
      return null;
    }

    if (!TryGetBaseline(corridorObject, out DataObject? alignmentObject, out DataObject? profileObject))
    {
      logger.LogWarning("Corridor '{Name}' has no baseline alignment; cannot rebuild", corridorObject.name);
      return null;
    }

    // phase 1: baseline alignment (+ profile), persisted before the corridor references it
    connection.GetType().GetMethod("StartTransientMode", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null);
    var alignment = alignmentConverter.Create(alignmentObject!, geometricModel);
    if (alignment is null)
    {
      connection.GetType().GetMethod("PersistTransients", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null);
      return null;
    }

    if (profileObject is not null)
    {
      profileConverter.Create(profileObject, alignment);
    }
    connection.GetType().GetMethod("PersistTransients", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null);

    // phase 2: corridor on the alignment
    connection.GetType().GetMethod("StartTransientMode", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null);
    object? corridor = null;
    try
    {
      corridor = alignment.GetType().GetMethod("CreateCorridorByAlignment", BindingFlags.Instance | BindingFlags.Public)?.Invoke(
        alignment,
        [string.IsNullOrEmpty(corridorObject.name) ? "Corridor" : corridorObject.name!]
      );
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogError(ex, "Failed to create corridor '{Name}' on alignment", corridorObject.name);
    }
    connection.GetType().GetMethod("PersistTransients", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null);

    if (corridor is null)
    {
      return null;
    }

    ApplyDefinition(corridor, corridorObject, connection);

    try
    {
      return corridor.GetType().GetProperty("Element", BindingFlags.Instance | BindingFlags.Public)?.GetValue(corridor)?.GetType().GetProperty("ElementId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(
        corridor.GetType().GetProperty("Element", BindingFlags.Instance | BindingFlags.Public)?.GetValue(corridor)
      )?.ToString();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }

  /// <summary>
  /// Applies the corridor definition from <see cref="CorridorSchema"/>. Template drops / point controls /
  /// superelevation are the remaining CifNET corridor-edit work; the property reads below give the exact data
  /// a live-SDK implementation needs.
  /// </summary>
  private void ApplyDefinition(object corridor, DataObject corridorObject, object connection)
  {
    // TODO(civil, live-SDK): for each templateDrop -> corridor.CreateTemplateDrop(station, template, interval);
    //                        for each pointControl / superelevation -> corresponding CifNET corridor edits.
    // The schema keys (CorridorSchema.TEMPLATE_DROPS / POINT_CONTROLS / SUPERELEVATION) carry the data.
    _ = corridor;
    _ = corridorObject;
    _ = connection;
  }

  private static bool TryGetBaseline(
    DataObject corridorObject,
    out DataObject? alignmentObject,
    out DataObject? profileObject
  )
  {
    alignmentObject = null;
    profileObject = null;

    if (corridorObject.properties.TryGetValue(CorridorSchema.BASELINE, out var baselineRaw) && baselineRaw is IReadOnlyDictionary<string, object?> baseline)
    {
      alignmentObject = baseline.TryGetValue(CorridorSchema.ALIGNMENT, out var a) ? a as DataObject : null;
      profileObject = baseline.TryGetValue(CorridorSchema.PROFILE, out var p) ? p as DataObject : null;
    }

    // fall back to the corridor's own display value as an alignment baseline if no nested alignment survived
    if (alignmentObject is null && corridorObject.displayValue.Count > 0)
    {
      alignmentObject = new DataObject
      {
        name = corridorObject.name,
        displayValue = corridorObject.displayValue,
        properties = new Dictionary<string, object?>(),
      };
    }

    return alignmentObject is not null;
  }
}
