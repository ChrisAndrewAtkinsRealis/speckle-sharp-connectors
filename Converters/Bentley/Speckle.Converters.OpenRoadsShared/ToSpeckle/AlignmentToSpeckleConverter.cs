using Speckle.Converters.Common;
using Speckle.Converters.Common.Civil;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation;
using Speckle.Converters.OpenRoads.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.ToSpeckle;

/// <summary>
/// Converts an OpenRoads/OpenRail horizontal alignment to a DataObject: display curves (from the alignment's
/// element graphics) plus civil properties (name, feature, stationing). Ported from the v2 ConverterBentley
/// civil converter and simplified to the v3 DataObject model.
/// </summary>
[NameAndRankValue(typeof(CifGM.Alignment), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class AlignmentToSpeckleConverter(
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  CivilHorizontalGeometryExtractor horizontalGeometryExtractor,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((CifGM.Alignment)target);

  public DataObject Convert(CifGM.Alignment target)
  {
    // display curves are always attached (visualization + rebuild fallback); the structured horizontal
    // geometry is what allows a faithful rebuild.
    var displayValue = CivilDisplayExtensions.GetDisplayValue(target.Element, curveVectorConverter);
    var properties = new Dictionary<string, object?>();

    var horizontalGeometry = horizontalGeometryExtractor.Extract(target);
    if (horizontalGeometry.Count > 0)
    {
      properties[AlignmentGeometrySchema.HORIZONTAL_GEOMETRY] = horizontalGeometry;
    }

    TrySet(properties, "featureName", () => target.FeatureName);
    TrySet(properties, "featureDefinition", () => target.FeatureDefinition?.Name);

    try
    {
      if (target.Stationing is { } stationing)
      {
        properties["startStation"] = stationing.StartStation;
        properties["endStation"] = target.LinearGeometry.Length + stationing.StartStation;
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // stationing is optional / may be unavailable on some alignments
    }

    return new DataObject
    {
      name = SafeName(target),
      displayValue = displayValue,
      properties = properties,
      ["type"] = "Alignment",
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
  }

  private static string SafeName(CifGM.Alignment alignment)
  {
    try
    {
      return alignment.Name ?? "Alignment";
    }
    catch (Exception)
    {
      return "Alignment";
    }
  }

  private static void TrySet(Dictionary<string, object?> properties, string key, Func<string?> read)
  {
    try
    {
      if (read() is string value && !string.IsNullOrEmpty(value))
      {
        properties[key] = value;
      }
    }
    catch (Exception)
    {
      // property not available on this entity
    }
  }
}
