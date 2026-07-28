using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Converts fallback-produced data objects flagged as solid/surface-like payloads.
/// This returns native displayable geometry (meshes/curves/points) so receive can bake it.
/// </summary>
[NameAndRankValue(typeof(DataObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 1)]
public class SolidLikeDataObjectToHostConverter(
  ITypedConverter<ICurve, BDE.DisplayableElement> curveConverter,
  ITypedConverter<SOG.Mesh, BDE.MeshHeaderElement> meshConverter,
  ITypedConverter<SOG.Point, BDE.LineElement> pointConverter
) : IToHostTopLevelConverter
{
  public object Convert(Base target)
  {
    var dataObject = (DataObject)target;
    if (!IsSolidLikeFallback(dataObject))
    {
      throw new ConversionNotSupportedException(
        "DataObject is not a fallback payload with a solid/surface-like MicroStation element type."
      );
    }

    var result = new List<(BDE.Element, Base)>();
    ConvertDataObject(dataObject, result);
    return result;
  }

  private static bool IsSolidLikeFallback(DataObject dataObject)
  {
    if (!dataObject.properties.TryGetValue("conversionKind", out object? conversionKind) || conversionKind is not string kind)
    {
      return false;
    }

    if (!dataObject.properties.TryGetValue("sourceMSElementType", out object? value) || value is not string typeName)
    {
      return false;
    }

    return kind == "fallback" && typeName is "Solid" or "Surface" or "Cone";
  }

  private void ConvertDataObject(DataObject target, List<(BDE.Element, Base)> result)
  {
    foreach (var displayObject in target.displayValue)
    {
      switch (displayObject)
      {
        case SOG.Mesh mesh:
          result.Add((meshConverter.Convert(mesh), target));
          break;
        case SOG.Point point:
          result.Add((pointConverter.Convert(point), target));
          break;
        case ICurve curve:
          result.Add((curveConverter.Convert(curve), target));
          break;
        case DataObject nested:
          ConvertDataObject(nested, result);
          break;
        default:
          throw new ConversionException($"Found unsupported display geometry: {displayObject.GetType()}");
      }
    }
  }
}
