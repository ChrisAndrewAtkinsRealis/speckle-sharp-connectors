using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.OpenRoads.Extensions;

public static class CivilDisplayExtensions
{
  /// <summary>
  /// Extracts display curves from a civil entity's underlying displayable element, reusing the MicroStation
  /// curve-vector converter. Civil entities (alignments, profiles, features) all expose a native
  /// <c>Element</c> whose graphics are the geometry we want to visualise.
  /// </summary>
  public static List<Base> GetDisplayValue(
    BDE.Element? element,
    ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter
  )
  {
    var displayValue = new List<Base>();
    if (element is BDE.DisplayableElement && element.GetCurveVectorOrNull() is BG.CurveVector vec)
    {
      displayValue.AddRange(curveVectorConverter.Convert(vec).Cast<Base>());
    }

    return displayValue;
  }
}
