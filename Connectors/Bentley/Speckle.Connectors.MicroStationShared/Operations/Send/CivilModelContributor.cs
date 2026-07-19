#if OPENROADS || OPENRAIL
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Real civil contributor for OpenRoads/OpenRail: enumerates civil entities and converts them (via the
/// converter manager, since civil entities are not native <c>Element</c>s) into a <c>Civil</c> collection.
/// </summary>
public sealed class CivilModelContributor : ICivilModelContributor
{
  private readonly CivilModelService _civilModelService;
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ILogger<CivilModelContributor> _logger;

  public CivilModelContributor(
    CivilModelService civilModelService,
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    ILogger<CivilModelContributor> logger
  )
  {
    _civilModelService = civilModelService;
    _toSpeckle = toSpeckle;
    _logger = logger;
  }

  public IReadOnlyList<SendConversionResult> Contribute(Collection root, CancellationToken cancellationToken)
  {
    var entities = _civilModelService.GetCivilEntities();
    if (entities.Count == 0)
    {
      return [];
    }

    var civilCollection = new Collection { name = "Civil" };
    civilCollection["isCivil"] = true;

    var results = new List<SendConversionResult>(entities.Count);
    foreach (var entity in entities)
    {
      cancellationToken.ThrowIfCancellationRequested();

      string sourceType = entity.GetType().Name;
      try
      {
        var converter = _toSpeckle.ResolveConverter(entity.GetType());
        Base converted = converter.Convert(entity);
        civilCollection.elements.Add(converted);
        results.Add(new(Status.SUCCESS, converted.applicationId ?? converted.id ?? sourceType, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to convert civil entity {SourceType}", sourceType);
        results.Add(new(Status.ERROR, sourceType, sourceType, null, ex));
      }
    }

    if (civilCollection.elements.Count > 0)
    {
      root.elements.Add(civilCollection);
    }

    return results;
  }
}
#endif
