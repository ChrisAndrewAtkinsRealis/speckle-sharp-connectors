using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.BSplineCurveElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BSplineCurveElementToSpeckleConverter(ITypedConverter<BG.MSBsplineCurve, SOG.Curve> bsplineConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.BSplineCurveElement)target);

  public SOG.Curve Convert(BDE.BSplineCurveElement target)
  {
    var vec =
      target.GetCurveVectorOrNull() ?? throw new ConversionException("B-spline curve element has no curve geometry.");

    var primitive = vec.GetPrimitive(0);
    var spline = primitive?.GetBsplineCurve() ?? primitive?.GetProxyBsplineCurve();
    if (spline is null)
    {
      throw new ConversionException("Could not extract b-spline data from curve element.");
    }

    return bsplineConverter.Convert(spline);
  }
}
