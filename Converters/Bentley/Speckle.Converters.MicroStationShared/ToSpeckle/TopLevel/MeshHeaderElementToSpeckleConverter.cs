using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.MeshHeaderElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshHeaderElementToSpeckleConverter(ITypedConverter<BG.PolyfaceHeader, SOG.Mesh> meshConverter)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.MeshHeaderElement)target);

  public SOG.Mesh Convert(BDE.MeshHeaderElement target)
  {
    using BG.PolyfaceHeader meshData =
      target.GetMeshData() ?? throw new ConversionException("Mesh element has no mesh data.");

    return meshConverter.Convert(meshData);
  }
}
