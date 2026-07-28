using System.Reflection;
using Speckle.Sdk;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// Best-effort extraction of Item Type-like data from a MicroStation element.
/// The Bentley API surface is host-version dependent, so this is intentionally defensive:
/// it uses reflection to look for common Item Type access patterns and never fails the conversion.
/// </summary>
public class ItemTypePropertiesExtractor
{
  public Dictionary<string, object?> GetProperties(BDE.Element element)
  {
    var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    if (element is null)
    {
      return properties;
    }

    try
    {
      CollectItemTypeData(element, properties);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // Item Type data is optional and should never break conversion.
    }

    return properties;
  }

  private static void CollectItemTypeData(object? source, IDictionary<string, object?> target)
  {
    if (source is null)
    {
      return;
    }

    var type = source.GetType();

    foreach (string propertyName in s_itemTypePropertyNames)
    {
      PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (property is null)
      {
        continue;
      }

      object? value = property.GetValue(source);
      if (value is null)
      {
        continue;
      }

      if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
      {
        continue;
      }

      if (value is IDictionary<string, object?> nestedDictionary)
      {
        foreach (var kvp in nestedDictionary)
        {
          if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
          {
            target[$"{propertyName}.{kvp.Key}"] = kvp.Value;
          }
        }
        continue;
      }

      if (value is IEnumerable<KeyValuePair<string, object?>> enumerableKvp)
      {
        foreach (var kvp in enumerableKvp)
        {
          if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
          {
            target[$"{propertyName}.{kvp.Key}"] = kvp.Value;
          }
        }
        continue;
      }

      if (value is IEnumerable<object?> enumerable && value is not string)
      {
        target[propertyName] = enumerable.Cast<object?>().Where(v => v is not null).Select(v => v).ToList();
        continue;
      }

      target[propertyName] = value;
    }

    foreach (string propertyName in s_nestedItemTypePropertyNames)
    {
      PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (property is null)
      {
        continue;
      }

      object? value = property.GetValue(source);
      if (value is not null)
      {
        CollectItemTypeData(value, target);
      }
    }
  }

  private static readonly string[] s_itemTypePropertyNames =
  [
    "ItemType",
    "ItemTypeName",
    "ItemTypeId",
    "ItemTypeProperties",
    "CustomItemHost",
    "ItemTypeDefinition",
    "ItemTypes",
    "ItemTypeLibrary"
  ];

  private static readonly string[] s_nestedItemTypePropertyNames =
  [
    "Definition",
    "DefinitionObject",
    "PropertyBag",
    "Properties",
    "Value"
  ];
}
