using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.SurfaceElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SurfaceElementToSpeckleConverter(ElementToSpeckleFallbackConverter fallbackConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.SurfaceElement)target);

  public DataObject Convert(BDE.SurfaceElement target) => fallbackConverter.Convert(target);
}
