using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToSpeckle.TopLevel;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

public class SolidElementToSpeckleRawConverter(ElementToSpeckleDataObjectBuilder builder)
  : ITypedConverter<BDE.SolidElement, Base>
{
  public Base Convert(BDE.SolidElement target) => builder.ConvertToSolidX(target, builder.IsParametricSolid(target));
}
