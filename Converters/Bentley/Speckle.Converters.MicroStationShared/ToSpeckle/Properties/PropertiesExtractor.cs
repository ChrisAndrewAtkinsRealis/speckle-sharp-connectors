using Bentley.DgnPlatformNET.DgnEC;
using Bentley.ECObjects.Instance;
using Bentley.ECObjects.Schema;
using Speckle.Sdk;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// Extracts EC (Engineering Content) instance data attached to an element into a property dictionary,
/// grouped by EC class name. This is intentionally defensive: EC data is host/schema dependent and we
/// never want property extraction to fail a conversion.
/// </summary>
public class PropertiesExtractor
{
  public Dictionary<string, object?> GetProperties(BDE.Element element)
  {
    var properties = new Dictionary<string, object?>();

    try
    {
      using DgnECInstanceCollection instances = DgnECManager.Manager.GetElementProperties(
        element,
        ECQueryProcessFlags.SearchAllClasses
      );

      foreach (IDgnECInstance instance in instances)
      {
        var group = ExtractInstance(instance);
        if (group.Count == 0)
        {
          continue;
        }

        string groupName = instance.ClassDefinition?.DisplayLabel ?? instance.ClassDefinition?.Name ?? "EC Data";
        if (!properties.ContainsKey(groupName))
        {
          properties[groupName] = group;
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // no EC data, or the EC framework refused us - not fatal for conversion
    }

    return properties;
  }

  private static Dictionary<string, object?> ExtractInstance(IDgnECInstance instance)
  {
    var group = new Dictionary<string, object?>();

    foreach (IECProperty property in instance.ClassDefinition)
    {
      try
      {
        IECPropertyValue? value = instance.GetPropertyValue(property.Name);
        if (value is null || value.IsNull)
        {
          continue;
        }

        object? extracted = ExtractValue(value);
        if (extracted is not null)
        {
          group[property.Name] = extracted;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // skip unreadable property
      }
    }

    return group;
  }

  private static object? ExtractValue(IECPropertyValue value)
  {
    if (value.TryGetDoubleValue(out double doubleValue))
    {
      return doubleValue;
    }

    if (value.TryGetIntValue(out int intValue))
    {
      return intValue;
    }

    if (value.TryGetStringValue(out string stringValue))
    {
      return stringValue;
    }

    return value.StringValue;
  }
}
