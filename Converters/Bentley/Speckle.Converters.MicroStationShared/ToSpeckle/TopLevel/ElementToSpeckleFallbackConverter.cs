using Bentley.DgnPlatformNET;
using Bentley.GeometryNET;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BDE.Element), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK - 100)]
public class ElementToSpeckleFallbackConverter(
  ITypedConverter<BG.PolyfaceHeader, SOG.Mesh> meshConverter,
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  ILogger<ElementToSpeckleFallbackConverter> logger
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((BDE.Element)target);

  public DataObject Convert(BDE.Element target)
  {
    var displayValue = new List<Base>();

    // try direct curve extraction first for curve-bearing elements
    var curveVector = target.GetCurveVectorOrNull();
    if (curveVector is not null)
    {
      displayValue.AddRange(curveVectorConverter.Convert(curveVector).Cast<Base>());
    }

    // then collect facet meshes from graphics processing (covers most displayable solids/surfaces/extended elements)
    var facets = new FacetCollectorProcessor();
    Exception? processingException = null;
    try
    {
      ElementGraphicsOutput.Process(target, facets);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      processingException = ex;
      logger.LogDebug(ex, "Failed to process display facets for {ElementType}", target.GetType().Name);
    }

    foreach (PolyfaceHeader polyface in facets.Meshes)
    {
      displayValue.Add(meshConverter.Convert(polyface));
    }

    if (displayValue.Count == 0)
    {
      var childDisplayValue = new List<Base>();
      foreach (BDE.Element child in EnumerateChildren(target))
      {
        if (TryCollectDisplayValue(child, childDisplayValue))
        {
          displayValue.AddRange(childDisplayValue);
          break;
        }
      }
    }

    if (displayValue.Count == 0)
    {
      throw new ConversionException(
        $"Unsupported MicroStation element type {target.GetType().Name}: no curve or mesh display geometry was extracted.",
        processingException
      );
    }

    string typeName = target.GetType().Name;
    string elementTypeName = target.ElementType.ToString();

    return new DataObject
    {
      name = typeName,
      displayValue = displayValue,
      properties = new Dictionary<string, object?>
      {
        ["sourceElementClass"] = typeName,
        ["sourceMSElementType"] = elementTypeName,
        ["sourceMSElementTypeValue"] = (int)target.ElementType,
        ["isExtendedElementType"] = IsExtendedElementType(elementTypeName),
        ["isParametricSolidType"] = IsParametricSolidType(elementTypeName),
      },
      ["type"] = typeName,
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
  }

  private bool TryCollectDisplayValue(BDE.Element target, List<Base> collectedDisplayValue)
  {
    var curveVector = target.GetCurveVectorOrNull();
    if (curveVector is not null)
    {
      collectedDisplayValue.AddRange(curveVectorConverter.Convert(curveVector).Cast<Base>());
      return collectedDisplayValue.Count > 0;
    }

    var facets = new FacetCollectorProcessor();
    try
    {
      ElementGraphicsOutput.Process(target, facets);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to process display facets for child {ElementType}", target.GetType().Name);
      return false;
    }

    foreach (PolyfaceHeader polyface in facets.Meshes)
    {
      collectedDisplayValue.Add(meshConverter.Convert(polyface));
    }

    if (collectedDisplayValue.Count > 0)
    {
      return true;
    }

    foreach (BDE.Element child in EnumerateChildren(target))
    {
      if (TryCollectDisplayValue(child, collectedDisplayValue))
      {
        return true;
      }
    }

    return false;
  }

  private static IEnumerable<BDE.Element> EnumerateChildren(BDE.Element element)
  {
    foreach (var child in element.GetChildren())
    {
      if (child is BDE.Element childElement && !childElement.IsInvisible)
      {
        yield return childElement;
      }
    }
  }

  private sealed class FacetCollectorProcessor : ElementGraphicsProcessor
  {
    public List<PolyfaceHeader> Meshes { get; } = new();

    public override bool ProcessAsBody(bool isCurved) => true;

    public override bool ProcessAsFacets(bool isPolyface) => true;

    public override bool WantClipping() => false;

    public override BentleyStatus ProcessSurface(MSBsplineSurface surface) => BentleyStatus.Success;

    public override BentleyStatus ProcessFacets(PolyfaceHeader meshData, bool filled)
    {
      // PolyfaceHeader is consumed immediately after processing; keep the same instance reference.
      Meshes.Add(meshData);
      return BentleyStatus.Success;
    }

    public override BentleyStatus ProcessCurveVector(CurveVector vector, bool isFilled) => BentleyStatus.Success;

    public override BentleyStatus ProcessCurvePrimitive(CurvePrimitive curvePrimitive, bool isClosed, bool isFilled) =>
      BentleyStatus.Success;
  }

  private static bool IsExtendedElementType(string elementTypeName) =>
    elementTypeName is "DgnStoreHeader"
      or "GroupData"
      or "Type4"
      or "Type44"
      or "DigSetData"
      or "TableEntry"
      or "View"
      or "ViewGroup";

  private static bool IsParametricSolidType(string elementTypeName) =>
    elementTypeName is "Solid" or "Cone";
}
