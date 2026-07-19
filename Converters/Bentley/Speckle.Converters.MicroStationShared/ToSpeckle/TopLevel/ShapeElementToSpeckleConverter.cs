using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.ShapeElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ShapeElementToSpeckleConverter(ITypedConverter<List<BG.DPoint3d>, SOG.Polyline> polylineConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.ShapeElement)target);

  public SOG.Polyline Convert(BDE.ShapeElement target)
  {
    var vec = target.GetCurveVectorOrNull() ?? throw new ConversionException("Shape element has no curve geometry.");

    var points = new List<BG.DPoint3d>();
    foreach (var primitive in vec)
    {
      var primitivePoints = new List<BG.DPoint3d>();
      if (primitive.TryGetLineString(primitivePoints))
      {
        points.AddRange(primitivePoints);
      }
    }

    if (points.Count == 0)
    {
      throw new ConversionException("Shape element has no points.");
    }

    var polyline = polylineConverter.Convert(points);
    polyline.closed = true;
    return polyline;
  }
}
