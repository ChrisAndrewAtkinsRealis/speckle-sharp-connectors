using Bentley.DgnPlatformNET.DgnEC;
using Bentley.ECObjects.Instance;
using Bentley.ECObjects.Schema;
using Speckle.Sdk;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// Shared scalar-value extraction for a DgnEC instance's properties, used by both the generic EC
/// <see cref="PropertiesExtractor"/> and the Item Type-specific <see cref="ItemTypePropertiesExtractor"/>.
/// </summary>
internal static class EcPropertyValueReader
{
  public static Dictionary<string, object?> ReadValues(IDgnECInstance instance)
  {
    var values = new Dictionary<string, object?>();

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
          values[property.Name] = extracted;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // skip unreadable property
      }
    }

    return values;
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
