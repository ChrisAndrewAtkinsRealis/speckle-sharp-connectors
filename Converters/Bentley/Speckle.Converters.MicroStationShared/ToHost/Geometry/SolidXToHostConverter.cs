using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Converts a received SolidX back into native MicroStation geometry. MicroStation's send-side SolidX
/// encoding carries no lossless raw geometry (<c>encodedValue.contents</c> is always empty), so this rebuilds
/// from the tessellated display meshes - a degraded but honest round-trip rather than a failed conversion.
/// </summary>
[NameAndRankValue(typeof(SOG.SolidX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidXToHostConverter(ITypedConverter<SOG.Mesh, BDE.MeshHeaderElement> meshConverter)
  : IToHostTopLevelConverter
{
  public object Convert(Base target) => Convert((SOG.SolidX)target);

  public List<(BDE.Element, Base)> Convert(SOG.SolidX target)
  {
    var result = new List<(BDE.Element, Base)>();
    foreach (SOG.Mesh mesh in target.displayValue)
    {
      result.Add((meshConverter.Convert(mesh), mesh));
    }

    return result;
  }
}
