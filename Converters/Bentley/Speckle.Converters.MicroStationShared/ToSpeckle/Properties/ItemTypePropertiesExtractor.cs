using Bentley.DgnPlatformNET.DgnEC;
using Bentley.DgnPlatformNET.Elements;
using Speckle.Sdk;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// Reads Item Type instances attached to a MicroStation element via <see cref="CustomItemHost"/>. This is
/// distinct from <see cref="PropertiesExtractor"/>, which reads every EC class on the element undifferentiated -
/// <c>CustomItemHost.CustomItems</c> returns only the Item Type instances, so Item Type values land in
/// their own namespaced <c>properties["Item Types"]</c> bag instead of being mixed in with generic EC data.
/// </summary>
/// <remarks>
/// Each Item Type's owning library name is captured alongside its values (under the <c>"library"</c> key) because
/// the receive-side <c>MicroStationItemTypeBaker</c> needs both the library and Item Type name to resolve the
/// same Item Type on receive via <c>CustomItemHost.GetCustomItem(string, string)</c>.
/// </remarks>
public class ItemTypePropertiesExtractor
{
  public Dictionary<string, object?> GetProperties(BDE.Element element)
  {
    var itemTypes = new Dictionary<string, object?>();

    if (element is null)
    {
      return itemTypes;
    }

    try
    {
      var host = new CustomItemHost(element, false);
      foreach (IDgnECInstance instance in host.CustomItems)
      {
        Dictionary<string, object?> values = EcPropertyValueReader.ReadValues(instance);
        if (values.Count == 0)
        {
          continue;
        }

        string itemTypeName = instance.ClassDefinition.DisplayLabel ?? instance.ClassDefinition.Name;
        string libraryName = instance.ClassDefinition.Schema.Name;

        itemTypes[itemTypeName] = new Dictionary<string, object?>
        {
          ["library"] = libraryName,
          ["properties"] = values,
        };
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // Item Type data is optional and should never break conversion.
    }

    return itemTypes;
  }
}
