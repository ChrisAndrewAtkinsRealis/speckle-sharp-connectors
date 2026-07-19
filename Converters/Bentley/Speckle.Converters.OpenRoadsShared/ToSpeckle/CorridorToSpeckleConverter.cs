using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation;
using Speckle.Converters.OpenRoads.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.ToSpeckle;

/// <summary>
/// Converts a corridor to a DataObject, capturing as much of the corridor *definition* as CifNET exposes so
/// that - longer term - corridors can be rebuilt from Speckle (in ORD, or in Civil 3D for true ORD&lt;-&gt;C3D
/// interop). See the "Corridor interoperability" note in the Bentley README for the neutral-schema goal.
/// </summary>
/// <remarks>
/// Captured now (defensively, since the CifNET corridor API is uncertain): display geometry, the baseline
/// alignment and active profile names, key stations, and corridor surface names. Still to add for full
/// rebuild: template drops keyed by station, point controls, superelevation/cant, and target surfaces - each
/// as structured, connector-neutral data both the ORD and C3D connectors can consume.
/// </remarks>
[NameAndRankValue(typeof(CifGM.Corridor), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CorridorToSpeckleConverter(
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((CifGM.Corridor)target);

  public DataObject Convert(CifGM.Corridor target)
  {
    var displayValue = CivilDisplayExtensions.GetDisplayValue(target.Element, curveVectorConverter);
    var properties = new Dictionary<string, object?>();

    Capture(() => properties["alignmentName"] = target.CorridorAlignment?.Name);
    Capture(() => properties["profileName"] = target.CorridorProfile?.Name);
    Capture(() => properties["keyStations"] = target.KeyStations?.ToList());
    Capture(() => properties["surfaces"] = target.CorridorSurfaces?.Select(s => s.Name).ToList());

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
      ["type"] = "Corridor",
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
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
