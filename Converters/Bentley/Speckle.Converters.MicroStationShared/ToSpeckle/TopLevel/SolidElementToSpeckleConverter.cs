using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.SolidElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidElementToSpeckleConverter(ElementToSpeckleFallbackConverter fallbackConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.SolidElement)target);

  public DataObject Convert(BDE.SolidElement target) => fallbackConverter.Convert(target);
}
