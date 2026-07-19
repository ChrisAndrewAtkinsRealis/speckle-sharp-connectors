using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Open polycurves are baked as complex chains; closed polycurves as complex shapes.
/// </summary>
[NameAndRankValue(typeof(SOG.Polycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolycurveToHostConverter(
  ITypedConverter<ICurve, BDE.DisplayableElement> curveConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Polycurve, BDE.DisplayableElement>
{
  public object Convert(Base target) => Convert((SOG.Polycurve)target);

  public BDE.DisplayableElement Convert(SOG.Polycurve target)
  {
    if (target.closed)
    {
      var complexShape = new BDE.ComplexShapeElement(settingsStore.Current.Model, null);
      foreach (var segment in target.segments)
      {
        complexShape.AddComponentElement(curveConverter.Convert(segment));
      }
      return complexShape;
    }

    var complexString = new BDE.ComplexStringElement(settingsStore.Current.Model, null);
    foreach (var segment in target.segments)
    {
      complexString.AddComponentElement(curveConverter.Convert(segment));
    }
    return complexString;
  }
}
