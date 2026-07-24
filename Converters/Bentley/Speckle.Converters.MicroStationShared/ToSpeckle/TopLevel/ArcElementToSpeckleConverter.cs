using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.ArcElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcElementToSpeckleConverter(ITypedConverter<BG.DEllipse3d, ICurve> ellipseConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.ArcElement)target);

  public Base Convert(BDE.ArcElement target)
  { 
    var vec = target.GetCurveVectorOrNull() ?? throw new ConversionException("Arc element has no curve geometry.");

    var primitive = vec.GetPrimitive(0);
    if (primitive is null || !primitive.TryGetArc(out BG.DEllipse3d arc))
    {
      throw new ConversionException("Could not extract arc data from arc element.");
    }

    return (Base)ellipseConverter.Convert(arc);
  }
}
