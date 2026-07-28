using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.SurfaceElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SurfaceElementToSpeckleConverter(ITypedConverter<BDE.SurfaceElement, Base> rawConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.SurfaceElement)target);

  public Base Convert(BDE.SurfaceElement target) => rawConverter.Convert(target);
}
