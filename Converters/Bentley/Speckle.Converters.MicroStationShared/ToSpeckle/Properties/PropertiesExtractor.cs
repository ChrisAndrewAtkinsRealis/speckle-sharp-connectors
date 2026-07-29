using Bentley.DgnPlatformNET.DgnEC;
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
        var group = EcPropertyValueReader.ReadValues(instance);
        if (group.Count == 0)
        {
          continue;
        }
        // force class definition to load, so we can get its label
        string groupName = instance.ClassDefinition.DisplayLabel ?? instance.ClassDefinition.Name ?? "EC Data";
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
}
