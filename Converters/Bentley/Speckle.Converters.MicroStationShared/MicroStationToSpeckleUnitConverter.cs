using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.MicroStation;

/// <summary>
/// Maps MicroStation master unit names (as returned by <c>UnitDefinition.GetName(true, true)</c>) to Speckle unit strings.
/// </summary>
public class MicroStationToSpeckleUnitConverter : IHostToSpeckleUnitConverter<string>
{
  private static readonly IReadOnlyDictionary<string, string> s_unitsMapping = Create();

  private static IReadOnlyDictionary<string, string> Create()
  {
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    dict["Millimeter"] = Units.Millimeters;
    dict["Millimeters"] = Units.Millimeters;
    dict["Centimeter"] = Units.Centimeters;
    dict["Centimeters"] = Units.Centimeters;
    dict["Meter"] = Units.Meters;
    dict["Meters"] = Units.Meters;
    dict["Kilometer"] = Units.Kilometers;
    dict["Kilometers"] = Units.Kilometers;
    dict["Inch"] = Units.Inches;
    dict["Inches"] = Units.Inches;
    dict["Foot"] = Units.Feet;
    dict["Feet"] = Units.Feet;
    dict["Yard"] = Units.Yards;
    dict["Yards"] = Units.Yards;
    dict["Mile"] = Units.Miles;
    dict["Miles"] = Units.Miles;
    return dict;
  }

  public string ConvertOrThrow(string hostUnit)
  {
    if (s_unitsMapping.TryGetValue(hostUnit, out string? value))
    {
      return value;
    }

    throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
  }
}
