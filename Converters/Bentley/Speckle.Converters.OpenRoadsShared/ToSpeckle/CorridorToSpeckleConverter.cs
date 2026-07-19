using Speckle.Converters.Common;
using Speckle.Converters.Common.Civil;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.MicroStation;
using Speckle.Converters.OpenRoads.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.ToSpeckle;

/// <summary>
/// Converts a corridor to a DataObject following the connector-neutral <see cref="CorridorSchema"/> so it can
/// be rebuilt from Speckle - in ORD or Civil 3D (see the "Corridor interoperability" note in the Bentley
/// README). Display geometry always travels as display value; the corridor definition travels in properties.
/// </summary>
/// <remarks>
/// Captured now (defensively - CifNET's corridor API is uncertain): the baseline alignment + active profile
/// (nested as their own DataObjects via the converter manager), key stations and surface names. Template
/// drops, point controls and superelevation/cant are the next definition data to populate on the same schema.
/// </remarks>
[NameAndRankValue(typeof(CifGM.Corridor), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CorridorToSpeckleConverter(
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((CifGM.Corridor)target);

  public DataObject Convert(CifGM.Corridor target)
  {
    var displayValue = CivilDisplayExtensions.GetDisplayValue(target.Element, curveVectorConverter);
    var properties = new Dictionary<string, object?>();

    // baseline = alignment (horizontal) + profile (vertical), nested as their own converted DataObjects
    var baseline = new Dictionary<string, object?>();
    Capture(() =>
    {
      if (target.CorridorAlignment is { } alignment)
      {
        baseline[CorridorSchema.ALIGNMENT] = ConvertNested(alignment);
      }
    });
    Capture(() =>
    {
      if (target.CorridorProfile is { } profile)
      {
        baseline[CorridorSchema.PROFILE] = ConvertNested(profile);
      }
    });
    if (baseline.Count > 0)
    {
      properties[CorridorSchema.BASELINE] = baseline;
    }

    Capture(() => properties[CorridorSchema.KEY_STATIONS] = target.KeyStations?.ToList());
    Capture(() => properties[CorridorSchema.SURFACES] = target.CorridorSurfaces?.Select(s => s.Name).ToList());

    // TODO(civil): populate CorridorSchema.TEMPLATE_DROPS / POINT_CONTROLS / SUPERELEVATION for full rebuild.

    string name;
    try
    {
      name = target.Name ?? "Corridor";
    }
    catch (Exception)
    {
      name = "Corridor";
    }

    return new DataObject
    {
      name = name,
      displayValue = displayValue,
      properties = properties,
      ["type"] = CorridorSchema.TYPE,
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
  }

  private Base? ConvertNested(object civilEntity)
  {
    try
    {
      return toSpeckle.ResolveConverter(civilEntity.GetType()).Convert(civilEntity);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }

  private static void Capture(Action set)
  {
    try
    {
      set();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // corridor sub-data is optional and API-version dependent; never fail the whole conversion for it
    }
  }
}
