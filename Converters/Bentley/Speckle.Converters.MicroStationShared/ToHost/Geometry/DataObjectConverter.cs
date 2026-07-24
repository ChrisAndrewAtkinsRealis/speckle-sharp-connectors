using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Converts a data object (from any v3 connector) by baking its display value geometry.
/// </summary>
[NameAndRankValue(typeof(DataObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DataObjectConverter(
  ITypedConverter<ICurve, BDE.DisplayableElement> curveConverter,
  ITypedConverter<SOG.Mesh, BDE.MeshHeaderElement> meshConverter,
  ITypedConverter<SOG.Point, BDE.LineElement> pointConverter
) : IToHostTopLevelConverter, ITypedConverter<DataObject, List<(BDE.Element a, Base b)>>
{
  public object Convert(Base target) => Convert((DataObject)target);

  public List<(BDE.Element a, Base b)> Convert(DataObject target)
  {
    var result = new List<(BDE.Element a, Base b)>();

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
          result.AddRange(Convert(nested));
          break;
        default:
          throw new ConversionException($"Found unsupported display geometry: {displayObject.GetType()}");
      }
    }

    return result;
  }
}
