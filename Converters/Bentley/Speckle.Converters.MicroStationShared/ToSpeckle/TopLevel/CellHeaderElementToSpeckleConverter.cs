using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a cell header by converting each of its displayable children and
/// aggregating the results as the display value of a single data object.
/// </summary>
[NameAndRankValue(typeof(BDE.CellHeaderElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CellHeaderElementToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> converterManager)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.CellHeaderElement)target);

  public DataObject Convert(BDE.CellHeaderElement target)
  {
    var displayValue = new List<Base>();

    foreach (var child in target.GetChildren())
    {
      if (child is not BDE.DisplayableElement displayableChild)
      {
        continue;
      }

      try
      {
        var childConverter = converterManager.ResolveConverter(displayableChild.GetType());
        displayValue.Add(childConverter.Convert(displayableChild));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // skip unconvertible children; the rest of the cell is still valuable
      }
    }

    return new DataObject
    {
      name = target.CellName ?? nameof(BDE.CellHeaderElement),
      displayValue = displayValue,
      properties = new Dictionary<string, object?>(),
    };
  }
}
