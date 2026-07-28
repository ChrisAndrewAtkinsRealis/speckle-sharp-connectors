using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.SolidElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidElementToSpeckleConverter(ITypedConverter<BDE.SolidElement, Base> rawConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.SolidElement)target);

  public Base Convert(BDE.SolidElement target) => rawConverter.Convert(target);
}
