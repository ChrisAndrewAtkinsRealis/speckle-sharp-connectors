using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Fallback conversion target used by <c>ConverterWithFallback</c>: bakes the display value
/// of objects that have no direct native conversion.
/// </summary>
[NameAndRankValue(typeof(DisplayableObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DisplayableObjectConverter(
  ITypedConverter<ICurve, BDE.DisplayableElement> curveConverter,
  ITypedConverter<SOG.Mesh, BDE.MeshHeaderElement> meshConverter,
  ITypedConverter<SOG.Point, BDE.LineElement> pointConverter
) : IToHostTopLevelConverter
{
  public object Convert(Base target) => Convert((DisplayableObject)target);

  public List<BDE.Element> Convert(DisplayableObject target)
  {
    var result = new List<BDE.Element>();

    foreach (var displayObject in target.displayValue)
    {
      switch (displayObject)
      {
        case SOG.Mesh mesh:
          result.Add(meshConverter.Convert(mesh));
          break;
        case SOG.Point point:
          result.Add(pointConverter.Convert(point));
          break;
        case ICurve curve:
          result.Add(curveConverter.Convert(curve));
          break;
        case DataObject nestedDataObject:
          foreach (var nested in nestedDataObject.displayValue)
          {
            switch (nested)
            {
              case SOG.Mesh nestedMesh:
                result.Add(meshConverter.Convert(nestedMesh));
                break;
              case SOG.Point nestedPoint:
                result.Add(pointConverter.Convert(nestedPoint));
                break;
              case ICurve nestedCurve:
                result.Add(curveConverter.Convert(nestedCurve));
                break;
              default:
                throw new ConversionException($"Found unsupported nested display geometry: {nested.GetType()}");
            }
          }
          break;
        default:
          throw new ConversionException($"Found unsupported display geometry: {displayObject.GetType()}");
      }
    }

    return result;
  }
}
