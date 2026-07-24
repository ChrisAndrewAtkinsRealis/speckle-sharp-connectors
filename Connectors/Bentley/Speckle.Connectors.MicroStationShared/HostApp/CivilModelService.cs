#if OPENROADS || OPENRAIL
using System.Reflection;
using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Enumerates civil (CifNET) entities from the active geometric model(s). OpenRoads/OpenRail only.
/// </summary>
/// <remarks>
/// Uses the ATRL/Atom ORD access pattern: <c>ConsensusConnectionEdit.GetActive().GetAllGeometricModels()</c>,
/// then each model's <c>Alignments</c> / <c>Corridors</c>. This is a CifNET-API-dependent surface and is
/// expected to need adjustment against a live OpenRoads/OpenRail SDK build.
/// </remarks>
public class CivilModelService
{
  private readonly ILogger<CivilModelService> _logger;

  public CivilModelService(ILogger<CivilModelService> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Returns the civil entities (alignments, corridors, features) across all active geometric models,
  /// boxed as objects so the caller can resolve a converter by runtime type.
  /// </summary>
  public IReadOnlyList<object> GetCivilEntities()
  {
    var entities = new List<object>();

    try
    {
      var connectionType = Type.GetType(
        "Bentley.CifNET.SDK.ConsensusConnection, Bentley.CifNET.SDK.4.0",
        throwOnError: false
      );
      var connection = connectionType?.GetMethod("GetActive", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
      if (connection is null)
      {
        return entities;
      }

      if (connection.GetType().GetMethod("GetAllGeometricModels", BindingFlags.Instance | BindingFlags.Public)?.Invoke(connection, null) is System.Collections.IEnumerable geometricModels)
      {
        foreach (var geometricModel in geometricModels)
        {
          if (geometricModel is null)
          {
            continue;
          }

          AddRange(entities, () => geometricModel.GetType().GetProperty("Alignments", BindingFlags.Instance | BindingFlags.Public)?.GetValue(geometricModel) as System.Collections.IEnumerable);
          AddRange(entities, () => geometricModel.GetType().GetProperty("Corridors", BindingFlags.Instance | BindingFlags.Public)?.GetValue(geometricModel) as System.Collections.IEnumerable);
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to enumerate civil entities from the active geometric model");
    }

    return entities;
  }

  private void AddRange(List<object> sink, Func<System.Collections.IEnumerable?> read)
  {
    try
    {
      if (read() is System.Collections.IEnumerable items)
      {
        foreach (var item in items)
        {
          if (item is not null)
          {
            sink.Add(item);
          }
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "A civil entity collection could not be read");
    }
  }
}
#endif
