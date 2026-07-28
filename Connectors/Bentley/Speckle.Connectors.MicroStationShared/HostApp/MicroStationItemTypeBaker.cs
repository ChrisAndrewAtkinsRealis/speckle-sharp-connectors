using System.Globalization;
using Bentley.DgnPlatformNET.DgnEC;
using Bentley.DgnPlatformNET.Elements;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Receive-side Item Type writeback for MicroStation elements, via <see cref="CustomItemHost"/>. Reads the
/// namespaced <c>properties["Item Types"]</c> bag written by
/// <c>Speckle.Converters.MicroStation.ToSpeckle.Properties.ItemTypePropertiesExtractor</c> and applies each
/// Item Type's values back onto the received element.
/// </summary>
/// <remarks>
/// An incoming Item Type is matched to an existing Item Type/library pair already defined in the target file via
/// <c>CustomItemHost.GetCustomItem(string, string)</c>. An Item Type with no match in the target file is
/// skipped and logged rather than attempting to author a new ItemTypeLibrary/ItemType definition - creating
/// definitions generically risks corrupting the file's Item Type libraries. See the "Risk" section of
/// Converters/Bentley/PLAN.md for the reasoning behind this "import only if already defined" decision.
/// </remarks>
public class MicroStationItemTypeBaker
{
  private readonly ILogger<MicroStationItemTypeBaker> _logger;

  public MicroStationItemTypeBaker(ILogger<MicroStationItemTypeBaker> logger)
  {
    _logger = logger;
  }

  public void ApplyItemTypes(BDE.Element element, Base source)
  {
    if (element is null || source is not DataObject dataObject)
    {
      return;
    }

    try
    {
      if (
        !dataObject.properties.TryGetValue("Item Types", out object? itemTypesValue)
        || itemTypesValue is not IDictionary<string, object?> itemTypes
        || itemTypes.Count == 0
      )
      {
        return;
      }

      var host = new CustomItemHost(element, false);
      foreach (var kvp in itemTypes)
      {
        ApplySingleItemType(host, element, kvp.Key, kvp.Value);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to apply Item Type data to MicroStation element {ElementId}", element.ElementId);
    }
  }

  private void ApplySingleItemType(CustomItemHost host, BDE.Element element, string itemTypeName, object? entryValue)
  {
    if (
      !TryParseItemTypeEntry(itemTypeName, entryValue, out string libraryName, out IDictionary<string, object?> values)
    )
    {
      return;
    }

    IDgnECInstance? instance = host.GetCustomItem(libraryName, itemTypeName);
    if (instance is null)
    {
      _logger.LogWarning(
        "Skipping Item Type '{ItemTypeName}' from library '{LibraryName}' on element {ElementId}: not defined in the target file.",
        itemTypeName,
        libraryName,
        element.ElementId
      );
      return;
    }

    foreach (var prop in values)
    {
      if (prop.Value is null)
      {
        continue;
      }

      try
      {
        string stringValue = prop.Value is IFormattable formattable
          ? formattable.ToString(null, CultureInfo.InvariantCulture)
          : prop.Value.ToString() ?? "";
        instance.SetString(prop.Key, stringValue);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogDebug(ex, "Failed to set Item Type property {Property} on {ItemTypeName}", prop.Key, itemTypeName);
      }
    }

    instance.WriteChanges();
  }

  /// <summary>
  /// Parses a single <c>properties["Item Types"]</c> entry, as written by <c>ItemTypePropertiesExtractor</c>:
  /// <c>{ "library": string, "properties": { ...values... } }</c>. Pulled out as a pure function so the shape
  /// contract between the extractor and this baker can be unit-tested without the Bentley SDK.
  /// </summary>
  public static bool TryParseItemTypeEntry(
    string itemTypeName,
    object? entryValue,
    out string libraryName,
    out IDictionary<string, object?> values
  )
  {
    libraryName = "";
    values = new Dictionary<string, object?>();

    if (string.IsNullOrWhiteSpace(itemTypeName) || entryValue is not IDictionary<string, object?> entry)
    {
      return false;
    }

    if (
      !entry.TryGetValue("library", out object? libraryObj)
      || libraryObj is not string library
      || string.IsNullOrWhiteSpace(library)
    )
    {
      return false;
    }

    if (
      !entry.TryGetValue("properties", out object? propertiesObj)
      || propertiesObj is not IDictionary<string, object?> parsedValues
    )
    {
      return false;
    }

    libraryName = library;
    values = parsedValues;
    return true;
  }
}
