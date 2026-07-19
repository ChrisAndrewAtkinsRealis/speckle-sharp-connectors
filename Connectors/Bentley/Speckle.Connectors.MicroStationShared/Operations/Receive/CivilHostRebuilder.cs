#if OPENROADS || OPENRAIL
using Bentley.CifNET.SDK.Edit;
using Microsoft.Extensions.Logging;
using Speckle.Converters.OpenRoads.ToHost;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.MicroStation.Operations.Receive;

/// <summary>
/// Real civil rebuilder for OpenRoads/OpenRail. Dispatches received Alignment/Corridor DataObjects to the
/// civil ToHost converters, which use the CifNET edit/transient API to regenerate native civil entities.
/// </summary>
public sealed class CivilHostRebuilder : ICivilHostRebuilder
{
  private const string TYPE_ALIGNMENT = "Alignment";
  private const string TYPE_CORRIDOR = "Corridor";

  private readonly AlignmentToHostConverter _alignmentConverter;
  private readonly CorridorToHostConverter _corridorConverter;
  private readonly ILogger<CivilHostRebuilder> _logger;

  public CivilHostRebuilder(
    AlignmentToHostConverter alignmentConverter,
    CorridorToHostConverter corridorConverter,
    ILogger<CivilHostRebuilder> logger
  )
  {
    _alignmentConverter = alignmentConverter;
    _corridorConverter = corridorConverter;
    _logger = logger;
  }

  public bool CanRebuild(Base obj) => obj is DataObject dataObject && GetCivilType(dataObject) is not null;

  public IReadOnlyList<string> Rebuild(Base obj)
  {
    var dataObject = (DataObject)obj;
    string? type = GetCivilType(dataObject);
    var connection = ConsensusConnectionEdit.GetActive();
    if (connection is null)
    {
      return [];
    }

    switch (type)
    {
      case TYPE_CORRIDOR:
        // corridor converter manages its own two-phase transient session
        string? corridorId = _corridorConverter.Create(dataObject, connection);
        return corridorId is null ? [] : new[] { corridorId };

      case TYPE_ALIGNMENT:
        connection.StartTransientMode();
        var alignment = _alignmentConverter.Create(dataObject, connection.GetOrCreateGeometricModel());
        connection.PersistTransients();
        return TryGetElementId(alignment) is string id ? new[] { id } : [];

      default:
        return [];
    }
  }

  private static string? GetCivilType(DataObject dataObject) =>
    dataObject["type"] is string type && type is TYPE_ALIGNMENT or TYPE_CORRIDOR ? type : null;

  private string? TryGetElementId(CifGM.Alignment? alignment)
  {
    try
    {
      return alignment?.Element?.ElementId.ToString();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Could not read rebuilt alignment element id");
      return null;
    }
  }
}
#endif
