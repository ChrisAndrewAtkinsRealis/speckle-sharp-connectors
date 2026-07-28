using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Best-effort receive-side Item Type writeback for MicroStation elements.
/// The Bentley API is version-dependent, so this baker is intentionally defensive and never throws
/// on missing Item Type support.
/// </summary>
public class MicroStationItemTypeBaker
{
  private readonly ILogger<MicroStationItemTypeBaker> _logger;

  public MicroStationItemTypeBaker(
    ILogger<MicroStationItemTypeBaker> logger
  )
  {
    _logger = logger;
  }

  public void ApplyItemTypes(BDE.Element element, Base source)
  {
    if (element is null || source is null)
    {
      return;
    }

    try
    {
      if (source is not { } baseSource)
      {
        return;
      }

      if (TryGetItemTypes(baseSource, out IDictionary<string, object?>? itemTypes) && itemTypes is not null)
      {
        foreach (var kvp in itemTypes)
        {
          ApplySingleItemType(element, kvp.Key, kvp.Value);
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to apply Item Type data to MicroStation element {ElementId}", element.ElementId);
    }
  }

  private static bool TryGetItemTypes(Base source, out IDictionary<string, object?>? itemTypes)
  {
    itemTypes = null;

    if (source is null)
    {
      return false;
    }

    if (source is IDictionary<string, object?> directDictionary)
    {
      itemTypes = directDictionary;
      return true;
    }

    try
    {
      var property = source.GetType().GetProperty("Item Types");
      if (property is null)
      {
        return false;
      }

      var propertyValue = property.GetValue(source);
      if (propertyValue is IDictionary<string, object?> dictionary)
      {
        itemTypes = dictionary;
        return true;
      }
    }
    catch (ArgumentException) when (!System.Diagnostics.Debugger.IsAttached)
    {
      // Item Types are optional and should never block receive.
    }
    catch (InvalidOperationException) when (!System.Diagnostics.Debugger.IsAttached)
    {
      // Item Types are optional and should never block receive.
    }
    catch (NotSupportedException) when (!System.Diagnostics.Debugger.IsAttached)
    {
      // Item Types are optional and should never block receive.
    }
    catch (MethodAccessException) when (!System.Diagnostics.Debugger.IsAttached)
    {
      // Item Types are optional and should never block receive.
    }

    return false;
  }

  private static void ApplySingleItemType(BDE.Element element, string itemTypeName, object? value)
  {
    if (string.IsNullOrWhiteSpace(itemTypeName) || value is null)
    {
      return;
    }

    try
    {
      var elementType = element.GetType();
      var property = elementType.GetProperty("ItemType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
      if (property is null)
      {
        return;
      }

      property.SetValue(element, value);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // Item Type writeback is optional and should never block receive.
    }
  }
}
