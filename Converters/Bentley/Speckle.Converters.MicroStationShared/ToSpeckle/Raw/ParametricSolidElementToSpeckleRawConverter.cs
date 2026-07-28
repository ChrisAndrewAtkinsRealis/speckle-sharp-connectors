using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

public class ParametricSolidElementToSpeckleRawConverter(
  Speckle.Converters.MicroStation.ToSpeckle.TopLevel.ElementToSpeckleDataObjectBuilder builder
) : ITypedConverter<BDE.SolidElement, Base>
{
  public Base Convert(BDE.SolidElement target) => builder.ConvertToSolidX(target, includeParametricValues: true);
}
