using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Rebuilds MicroStation shared cells from Speckle instance proxies + definitions on receive. Scoped per
/// receive operation.
/// </summary>
/// <remarks>
/// Both shared cells and (one-off) normal cells are baked as shared cells here, so they always land as
/// instances that share a single definition. The native shared-cell definition/placement creation calls are
/// the least-verified surface of the connector and are the most likely to need adjustment against a live
/// MicroStation SDK build; they are isolated to <see cref="CreateDefinition"/> and <see cref="CreateInstance"/>.
/// </remarks>
public class MicroStationInstanceBaker : IInstanceBaker<List<BDE.Element>>
{
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _settingsStore;
  private readonly ILogger<MicroStationInstanceBaker> _logger;

  public MicroStationInstanceBaker(
    IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
    ILogger<MicroStationInstanceBaker> logger
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public BakeResult BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, List<BDE.Element>> applicationIdMap,
    string baseLayerName,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    // deepest first, and each definition before the instances that depend on it
    var sorted = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth)
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1)
      .ToList();

    var definitionsById = new Dictionary<string, (BDE.Element definitionElement, string definitionName)>();
    var conversionResults = new HashSet<ReceiveConversionResult>();
    var createdObjectIds = new HashSet<string>();
    var consumedObjectIds = new HashSet<string>();
    int count = 0;

    foreach (var (_, component) in sorted)
    {
      try
      {
        onOperationProgressed.Report(new("Converting cells", (double)++count / sorted.Count));

        switch (component)
        {
          case InstanceDefinitionProxy { applicationId: not null } definitionProxy:
          {
            var constituents = definitionProxy
              .objects.Where(applicationIdMap.ContainsKey)
              .SelectMany(id => applicationIdMap[id])
              .ToList();

            if (constituents.Count == 0)
            {
              throw new ConversionException("No objects found to create shared cell definition.");
            }

            string definitionName = $"{definitionProxy.name}-{definitionProxy.applicationId}-{baseLayerName}";
            BDE.Element definitionElement = CreateDefinition(definitionName, constituents);
            definitionsById[definitionProxy.applicationId] = (definitionElement, definitionName);

            var consumed = constituents.Select(e => e.ElementId.ToString()).ToArray();
            consumedObjectIds.UnionWith(consumed);
            createdObjectIds.RemoveWhere(id => consumed.Contains(id));
            break;
          }

          case InstanceProxy instanceProxy
            when definitionsById.TryGetValue(instanceProxy.definitionId, out var definition):
          {
            string instanceId = instanceProxy.applicationId ?? instanceProxy.id.NotNull();
            var element = CreateInstance(definition.definitionElement, definition.definitionName, instanceProxy);

            applicationIdMap[instanceId] = new List<BDE.Element> { element };
            createdObjectIds.Add(element.ElementId.ToString());
            conversionResults.Add(new(Status.SUCCESS, instanceProxy, element.ElementId.ToString(), "Instance (Cell)"));
            break;
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to bake a MicroStation cell from proxy");
        conversionResults.Add(new(Status.ERROR, component as Base ?? new Base(), null, null, ex));
      }
    }

    return new(createdObjectIds, consumedObjectIds, conversionResults);
  }

  public void PurgeInstances(string namePrefix)
  {
    // NOTE: purging previously-baked shared cell definitions by name prefix is a follow-up. Re-receiving
    // currently appends new definitions rather than replacing prior ones.
  }

  /// <summary>
  /// Creates a shared cell definition from already-baked constituent elements and adds it to the model.
  /// Isolated because the native creation API is the least-verified surface.
  /// </summary>
  private BDE.Element CreateDefinition(string definitionName, IReadOnlyList<BDE.Element> constituents)
  {
    var model = _settingsStore.Current.Model;
    using var definition = new BDE.SharedCellDefinitionElement(model, definitionName);

    foreach (var element in constituents)
    {
      definition.AddChildElement(element);
    }

    definition.AddToModel();
    return model.FindElementById(definition.ElementId)
      ?? throw new ConversionException($"Could not resolve shared cell definition '{definitionName}' after creation.");
  }

  /// <summary>
  /// Places a shared cell instance referencing <paramref name="definitionName"/> at the proxy's transform.
  /// </summary>
  private BDE.SharedCellElement CreateInstance(BDE.Element definition, string definitionName, InstanceProxy instanceProxy)
  {
    var model = _settingsStore.Current.Model;

    // convert the Speckle instance transform back to native UoRs, applying unit scaling if the instance was
    // authored in different units than the active model
    double unitScale = Units.GetConversionFactor(instanceProxy.units, _settingsStore.Current.SpeckleUnits);
    double translationScaleToUor = unitScale * _settingsStore.Current.UorPerMaster;
    var transform = MicroStationTransformHelper.ToNativeTransform(instanceProxy.transform, translationScaleToUor);

    var origin = transform.Translation;
    var rotation = transform.Matrix;
    var scale = new Bentley.GeometryNET.DPoint3d(1, 1, 1);

    var instance = new BDE.SharedCellElement(model, definition, definitionName, origin, rotation, scale);
    instance.AddToModel();
    return instance;
  }
}
