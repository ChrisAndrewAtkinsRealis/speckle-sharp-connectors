using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.LineElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineElementToSpeckleConverter(
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  ITypedConverter<BG.DSegment3d, SOG.Line> segmentConverter
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.LineElement)target);

  public Base Convert(BDE.LineElement target)
  {
    var vec =
      target.GetCurveVectorOrNull() ?? throw new ConversionException("Line element has no curve geometry.");

    vec.GetStartEnd(out BG.DPoint3d startPoint, out BG.DPoint3d endPoint);

    // degenerate (zero-length) lines are used as points in MicroStation
    if (startPoint.IsAlmostEqualTo(endPoint, 1e-9))
    {
      return pointConverter.Convert(startPoint);
    }

    return segmentConverter.Convert(new BG.DSegment3d(startPoint, endPoint));
  }
}
