using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Sdk;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Unpacks MicroStation cells into Speckle instance proxies + definitions on send, so they round-trip as
/// instances (like AutoCAD blocks) instead of being flattened. Scoped per send operation.
/// </summary>
/// <remarks>
/// Cell type mapping:
/// <list type="bullet">
/// <item><b>Shared cell</b> (<see cref="BDE.SharedCellElement"/>): true instancing. Many placements reference
/// one <see cref="BDE.SharedCellDefinitionElement"/>, so the definition id is the shared definition (its name),
/// shared across all placements.</item>
/// <item><b>Normal / orphan cell</b> (<see cref="BDE.CellHeaderElement"/>): not shared, so each placement becomes
/// its own one-off definition keyed by the cell's element id.</item>
/// <item><b>Parametric cell</b>: a shared cell driven by parameters. Handled via the shared-cell path, with the
/// parameter values captured on the instance proxy's <c>properties</c>.</item>
/// </list>
/// </remarks>
public class MicroStationInstanceUnpacker : IInstanceUnpacker<MicroStationRootObject>
{
  private readonly IInstanceObjectsManager<MicroStationRootObject, List<BDE.Element>> _instanceObjectsManager;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _settingsStore;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly ILogger<MicroStationInstanceUnpacker> _logger;
  private readonly Dictionary<string, BDE.SharedCellDefinitionElement> _sharedCellDefinitionsByName =
    new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, BDE.SharedCellDefinitionElement> _sharedCellDefinitionsById =
    new(StringComparer.Ordinal);
  private bool _sharedCellDefinitionsIndexed;

  public MicroStationInstanceUnpacker(
    IInstanceObjectsManager<MicroStationRootObject, List<BDE.Element>> instanceObjectsManager,
    IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor,
    ILogger<MicroStationInstanceUnpacker> logger
  )
  {
    _instanceObjectsManager = instanceObjectsManager;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
    _logger = logger;
  }

  public UnpackResult<MicroStationRootObject> UnpackSelection(IEnumerable<MicroStationRootObject> objects)
  {
    foreach (var obj in objects)
    {
      switch (obj.Root)
      {
        case BDE.SharedCellElement sharedCell:
          UnpackSharedCell(sharedCell, 0);
          break;
        case BDE.CellHeaderElement cell:
          UnpackCell(cell, 0);
          break;
      }

      _instanceObjectsManager.AddAtomicObject(obj.ApplicationId, obj);
    }

    return _instanceObjectsManager.GetUnpackResult();
  }

  private void UnpackSharedCell(BDE.SharedCellElement instance, int depth)
  {
    try
    {
      string instanceId = instance.ElementId.ToString();
      string definitionName = instance.CellName ?? instanceId;
      string definitionId = "shared-" + definitionName;

      // Resolve the true definition first: every placement shares one InstanceDefinitionProxy and gets its
      // own transform applied on top, so falling back to this placement's own (already-placed) children would
      // double-transform every sibling placement of the same cell once there's more than one to collide.
      var definitionElement = FindSharedCellDefinition(instance, definitionName);
      if (definitionElement is null)
      {
        _logger.LogWarning(
          "Could not locate shared cell definition {DefinitionName}; sending placement {InstanceId} as non-instanced geometry",
          definitionName,
          instanceId
        );
        return;
      }

      AddInstanceProxy(
        instanceId,
        definitionId,
        depth,
        instance,
        isParametric: TryGetParameters(instance, out var p),
        p
      );

      // definition is shared across placements: only unpack it once
      if (_instanceObjectsManager.TryGetInstanceDefinitionProxy(definitionId, out _))
      {
        return;
      }

      UnpackDefinition(definitionId, definitionName, depth, EnumerateChildren(definitionElement));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed unpacking MicroStation shared cell");
    }
  }

  private void UnpackCell(BDE.CellHeaderElement cell, int depth)
  {
    try
    {
      string instanceId = cell.ElementId.ToString();
      // normal cells are not shared: the definition is unique to this placement
      string definitionId = "cell-" + instanceId;
      string definitionName = "Cell-" + instanceId;

      AddInstanceProxy(instanceId, definitionId, depth, cell, isParametric: false, null);
      UnpackDefinition(definitionId, definitionName, depth, EnumerateChildren(cell));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed unpacking MicroStation cell");
    }
  }

  private void AddInstanceProxy(
    string instanceId,
    string definitionId,
    int depth,
    BDE.Element instance,
    bool isParametric,
    Dictionary<string, object?>? parameters
  )
  {
    var transform = MicroStationTransformHelper.GetElementTransform(instance);

    var instanceProxy = new InstanceProxy
    {
      applicationId = instanceId,
      definitionId = definitionId,
      maxDepth = depth,
      transform = MicroStationTransformHelper.ToInstanceMatrix(transform, _settingsStore.Current.UorPerMaster),
      units = _settingsStore.Current.SpeckleUnits,
    };

    var properties = _propertiesExtractor.GetProperties(instance);
    if (isParametric && parameters is { Count: > 0 })
    {
      properties["Parameters"] = parameters;
    }
    if (properties.Count > 0)
    {
      instanceProxy["properties"] = properties;
    }

    _instanceObjectsManager.AddInstanceProxy(instanceId, instanceProxy);

    // track all instances sharing this definition so their maxDepth stays consistent (needed for receive ordering)
    if (!_instanceObjectsManager.TryGetInstanceProxiesFromDefinitionId(definitionId, out List<InstanceProxy>? siblings))
    {
      siblings = new List<InstanceProxy>();
      _instanceObjectsManager.AddInstanceProxiesByDefinitionId(definitionId, siblings);
    }

    foreach (var sibling in siblings)
    {
      if (sibling.maxDepth < depth)
      {
        sibling.maxDepth = depth;
      }
    }

    siblings.Add(_instanceObjectsManager.GetInstanceProxy(instanceId));
  }

  private void UnpackDefinition(string definitionId, string name, int depth, IEnumerable<BDE.Element> children)
  {
    var definitionProxy = new InstanceDefinitionProxy
    {
      applicationId = definitionId,
      objects = new List<string>(),
      maxDepth = depth,
      name = name,
    };

    foreach (var child in children)
    {
      string childId = child.ElementId.ToString();
      definitionProxy.objects.Add(childId);

      // recurse into nested cells so nested instancing is preserved
      switch (child)
      {
        case BDE.SharedCellElement nestedShared:
          UnpackSharedCell(nestedShared, depth + 1);
          break;
        case BDE.CellHeaderElement nestedCell:
          UnpackCell(nestedCell, depth + 1);
          break;
      }

      _instanceObjectsManager.AddAtomicDefinitionObjectId(childId);
      _instanceObjectsManager.AddAtomicObject(childId, new MicroStationRootObject(child, childId));
    }

    _instanceObjectsManager.AddDefinitionProxy(definitionId, definitionProxy);
  }

  private static IEnumerable<BDE.Element> EnumerateChildren(BDE.Element cell)
  {
    // Walk the child tree recursively so cell-contained geometry nested in groups/containers is still sent.
    var pending = new Stack<BDE.Element>();
    foreach (var child in cell.GetChildren())
    {
      if (child is BDE.Element element && !element.IsInvisible)
      {
        pending.Push(element);
      }
    }

    while (pending.Count > 0)
    {
      BDE.Element current = pending.Pop();
      yield return current;

      foreach (var child in current.GetChildren())
      {
        if (child is BDE.Element element && !element.IsInvisible)
        {
          pending.Push(element);
        }
      }
    }
  }

  private BDE.SharedCellDefinitionElement? FindSharedCellDefinition(BDE.SharedCellElement instance, string name)
  {
    try
    {
      EnsureSharedCellDefinitionsIndexed();

      string definitionId = instance.GetDefinitionId().ToString();
      if (_sharedCellDefinitionsById.TryGetValue(definitionId, out BDE.SharedCellDefinitionElement? byId))
      {
        return byId;
      }

      return _sharedCellDefinitionsByName.TryGetValue(name, out BDE.SharedCellDefinitionElement? byName)
        ? byName
        : null;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed searching for shared cell definition {Name}", name);
      return null;
    }
  }

  private void EnsureSharedCellDefinitionsIndexed()
  {
    if (_sharedCellDefinitionsIndexed)
    {
      return;
    }

    foreach (BDE.Element element in _settingsStore.Current.Model.GetElements())
    {
      if (element is not BDE.SharedCellDefinitionElement definition)
      {
        continue;
      }

      string? cellName = definition.CellName;
      if (!string.IsNullOrWhiteSpace(cellName) && !_sharedCellDefinitionsByName.ContainsKey(cellName))
      {
        _sharedCellDefinitionsByName[cellName] = definition;
      }

      string definitionId = definition.ElementId.ToString();
      if (!_sharedCellDefinitionsById.ContainsKey(definitionId))
      {
        _sharedCellDefinitionsById[definitionId] = definition;
      }
    }

    _sharedCellDefinitionsIndexed = true;
    _logger.LogDebug(
      "Indexed {DefinitionCount} shared cell definitions by name and {DefinitionIdCount} by id",
      _sharedCellDefinitionsByName.Count,
      _sharedCellDefinitionsById.Count
    );
  }

  /// <summary>
  /// Attempts to read parametric cell variables (item-type / parameter driven cells) as a property bag.
  /// Returns false for plain shared cells.
  /// </summary>
  private bool TryGetParameters(BDE.SharedCellElement instance, out Dictionary<string, object?> parameters)
  {
    parameters = new Dictionary<string, object?>();
    try
    {
      // Parametric cells expose their variation/parameter values through EC instance data, which the
      // properties extractor already surfaces. Anything under a "Parameters"/"Variables" EC class is captured.
      var all = _propertiesExtractor.GetProperties(instance);
      foreach (var kvp in all)
      {
        string key = kvp.Key;
        if (key.IndexOf("Parameter", StringComparison.OrdinalIgnoreCase) >= 0
          || key.IndexOf("Variable", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          parameters[key] = kvp.Value;
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed reading parametric cell parameters");
    }

    return parameters.Count > 0;
  }
}
