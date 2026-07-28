using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

public class SurfaceElementToSpeckleRawConverter(
  Speckle.Converters.MicroStation.ToSpeckle.TopLevel.ElementToSpeckleDataObjectBuilder builder
) : ITypedConverter<BDE.SurfaceElement, Base>
{
  public Base Convert(BDE.SurfaceElement target) => builder.ConvertToSurfaceOrGraphicOrDataObject(target);
}
