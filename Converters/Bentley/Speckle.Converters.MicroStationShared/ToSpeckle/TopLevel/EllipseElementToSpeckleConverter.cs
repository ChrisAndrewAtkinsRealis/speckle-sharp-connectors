using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.EllipseElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EllipseElementToSpeckleConverter(ITypedConverter<BG.DEllipse3d, ICurve> ellipseConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.EllipseElement)target);

  public Base Convert(BDE.EllipseElement target)
  {
    var vec =
      target.GetCurveVectorOrNull() ?? throw new ConversionException("Ellipse element has no curve geometry.");

    var primitive = vec.GetPrimitive(0);
    if (primitive is null || !primitive.TryGetArc(out BG.DEllipse3d ellipse))
    {
      throw new ConversionException("Could not extract ellipse data from ellipse element.");
    }

    return (Base)ellipseConverter.Convert(ellipse);
  }
}
