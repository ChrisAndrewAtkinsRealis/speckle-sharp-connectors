using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.MicroStation.ToHost.Raw;

/// <summary>
/// Dispatches any Speckle curve to the correct native displayable element converter.
/// </summary>
public class ICurveToHostRawConverter(
  ITypedConverter<SOG.Line, BDE.LineElement> lineConverter,
  ITypedConverter<SOG.Polyline, BDE.DisplayableElement> polylineConverter,
  ITypedConverter<SOG.Arc, BDE.ArcElement> arcConverter,
  ITypedConverter<SOG.Circle, BDE.EllipseElement> circleConverter,
  ITypedConverter<SOG.Ellipse, BDE.EllipseElement> ellipseConverter,
  ITypedConverter<SOG.Curve, BDE.BSplineCurveElement> curveConverter,
  ITypedConverter<SOG.Polycurve, BDE.DisplayableElement> polycurveConverter
) : ITypedConverter<ICurve, BDE.DisplayableElement>
{
  public BDE.DisplayableElement Convert(ICurve target) =>
    target switch
    {
      SOG.Line line => lineConverter.Convert(line),
      SOG.Polyline polyline => polylineConverter.Convert(polyline),
      SOG.Arc arc => arcConverter.Convert(arc),
      SOG.Circle circle => circleConverter.Convert(circle),
      SOG.Ellipse ellipse => ellipseConverter.Convert(ellipse),
      SOG.Curve curve => curveConverter.Convert(curve),
      SOG.Polycurve polycurve => polycurveConverter.Convert(polycurve),
      _ => throw new ConversionNotSupportedException($"Unsupported curve type: {target.GetType().Name}"),
    };
}
