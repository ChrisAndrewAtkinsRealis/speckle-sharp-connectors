using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.ComplexStringElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ComplexStringElementToSpeckleConverter(
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.ComplexStringElement)target);

  public SOG.Polycurve Convert(BDE.ComplexStringElement target)
  {
    var vec =
      target.GetCurveVectorOrNull() ?? throw new ConversionException("Complex string element has no curve geometry.");

    return new SOG.Polycurve
    {
      segments = curveVectorConverter.Convert(vec),
      closed = vec.IsClosedPath,
      length = vec.SumOfLengths() / settingsStore.Current.UorPerMaster,
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
