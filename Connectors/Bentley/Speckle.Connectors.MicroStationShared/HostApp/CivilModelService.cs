#if OPENROADS || OPENRAIL
using Bentley.CifNET.SDK.Edit;
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
      var connection = ConsensusConnectionEdit.GetActive();
      if (connection is null)
      {
        return entities;
      }

      foreach (var geometricModel in connection.GetAllGeometricModels())
      {
        AddRange(entities, () => geometricModel.Alignments);
        AddRange(entities, () => geometricModel.Corridors);
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
