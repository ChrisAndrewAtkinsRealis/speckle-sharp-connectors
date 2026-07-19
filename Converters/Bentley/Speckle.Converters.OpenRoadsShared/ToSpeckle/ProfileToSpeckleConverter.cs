using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation;
using Speckle.Converters.OpenRoads.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.ToSpeckle;

/// <summary>
/// Converts a vertical profile to a DataObject (display curves + feature properties).
/// </summary>
[NameAndRankValue(typeof(CifGM.Profile), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ProfileToSpeckleConverter(
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((CifGM.Profile)target);

  public DataObject Convert(CifGM.Profile target)
  {
    var displayValue = CivilDisplayExtensions.GetDisplayValue(target.Element, curveVectorConverter);
    var properties = new Dictionary<string, object?>();

    try
    {
      if (target.FeatureName is string featureName && !string.IsNullOrEmpty(featureName))
      {
        properties["featureName"] = featureName;
      }
      if (target.FeatureDefinition?.Name is string featureDefinition)
      {
        properties["featureDefinition"] = featureDefinition;
      }
    }
    catch (Exception)
    {
      // feature metadata optional
    }

    string name;
    try
    {
      name = target.Name ?? "Profile";
    }
    catch (Exception)
    {
      name = "Profile";
    }

    return new DataObject
    {
      name = name,
      displayValue = displayValue,
      properties = properties,
      ["type"] = "Profile",
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
  }
}
