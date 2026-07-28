using ReflectionAssembly = System.Reflection.Assembly;
using ReflectionBindingFlags = System.Reflection.BindingFlags;
using ReflectionConstructorInfo = System.Reflection.ConstructorInfo;
using ReflectionMethodInfo = System.Reflection.MethodInfo;
using Bentley.DgnPlatformNET;
using Bentley.GeometryNET;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.Extensions;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

public class ElementToSpeckleDataObjectBuilder(
  ITypedConverter<BG.PolyfaceHeader, SOG.Mesh> meshConverter,
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  ITypedConverter<BG.DPoint3d, SOG.Point> pointConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  ILogger<ElementToSpeckleDataObjectBuilder> logger
)
{
  private readonly double _facetChordTolerance = ResolveToleranceEnvironmentValue(
    "SPECKLE_MICROSTATION_FACET_CHORD_TOLERANCE",
    0
  );

  private readonly double _facetAngleTolerance = ResolveToleranceEnvironmentValue(
    "SPECKLE_MICROSTATION_FACET_ANGLE_TOLERANCE",
    0.35
  );

  private readonly double _facetMaxEdgeLength = ResolveToleranceEnvironmentValue(
    "SPECKLE_MICROSTATION_FACET_MAX_EDGE_LENGTH",
    0
  );

  public Base ConvertToGraphicOrDataObject(BDE.Element target, string conversionKind, bool isParametricSolid = false)
  {
    DataObject dataObject = Convert(target, conversionKind, isParametricSolid);
    return TryExtractGraphicObject(dataObject, out Base? graphicObject) ? graphicObject! : dataObject;
  }

  public Base ConvertToSolidX(BDE.SolidElement target, bool includeParametricValues)
  {
    DataObject dataObject = Convert(target, includeParametricValues ? "parametric-solid" : "solid", includeParametricValues);
    var meshes = dataObject.displayValue.OfType<SOG.Mesh>().ToList();

    if (meshes.Count == 0)
    {
      meshes = CollectFacetMeshesFromElementAndChildren(target);
    }

    if (meshes.Count == 0)
    {
      logger.LogDebug("No mesh displayValue extracted for {ElementType}; sending SolidX without display meshes", target.GetType().Name);
    }

    var solid = new SOG.SolidX
    {
      displayValue = meshes,
      encodedValue = new RawEncoding { format = "microstation", contents = string.Empty },
      units = settingsStore.Current.SpeckleUnits,
    };

    if (includeParametricValues)
    {
      var parametricValues = new DataObject
      {
        name = "MicroStation Parametric Values",
        displayValue = [],
        properties = dataObject.properties,
        ["type"] = "MicroStation.ParametricValues",
        ["units"] = settingsStore.Current.SpeckleUnits,
      };

      solid["parametricValues"] = parametricValues;
    }

    return solid;
  }

  public Base ConvertToSurfaceOrGraphicOrDataObject(BDE.SurfaceElement target)
  {
    if (TryGetSurfaceGeometry(target, out SOG.Surface? surface))
    {
      return surface!;
    }

    return ConvertToGraphicOrDataObject(target, "surface");
  }

  public DataObject Convert(BDE.Element target, string conversionKind, bool isParametricSolid = false)
  {
    var displayValue = new List<Base>();

    TryAddCurveDisplayValue(target, displayValue);
    TryAddFacetDisplayValue(target, displayValue);

    if (displayValue.Count == 0)
    {
      foreach (BDE.Element child in EnumerateChildren(target))
      {
        var childDisplayValue = new List<Base>();
        if (TryCollectDisplayValue(child, childDisplayValue))
        {
          displayValue.AddRange(childDisplayValue);
        }
      }
    }

    string typeName = target.GetType().Name;
    string elementTypeName = target.ElementType.ToString();

    return new DataObject
    {
      name = typeName,
      displayValue = displayValue,
      properties = CreateProperties(target, conversionKind, typeName, elementTypeName, isParametricSolid),
      ["type"] = typeName,
      ["units"] = settingsStore.Current.SpeckleUnits,
    };
  }

  private Dictionary<string, object?> CreateProperties(
    BDE.Element target,
    string conversionKind,
    string typeName,
    string elementTypeName,
    bool isParametricSolid
  )
  {
    var properties = new Dictionary<string, object?>
    {
      ["sourceElementClass"] = typeName,
      ["sourceMSElementType"] = elementTypeName,
      ["sourceMSElementTypeValue"] = (int)target.ElementType,
      ["conversionKind"] = conversionKind,
      ["isExtendedElementType"] = IsExtendedElementType(elementTypeName),
      ["isParametricSolidType"] = isParametricSolid || IsParametricSolidType(elementTypeName),
    };

    if (target is BDE.SolidElement solidElement)
    {
      var primitiveValues = ExtractPrimitiveValues(solidElement);
      if (primitiveValues.Count > 0)
      {
        properties["parametricValues"] = primitiveValues;
      }
    }

    return properties;
  }

  private bool TryCollectDisplayValue(BDE.Element target, List<Base> collectedDisplayValue)
  {
    TryAddCurveDisplayValue(target, collectedDisplayValue);
    TryAddFacetDisplayValue(target, collectedDisplayValue);

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

  private void TryAddCurveDisplayValue(BDE.Element target, List<Base> displayValue)
  {
    var curveVector = target.GetCurveVectorOrNull();
    if (curveVector is null)
    {
      return;
    }

    try
    {
      displayValue.AddRange(curveVectorConverter.Convert(curveVector).Cast<Base>());
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to convert curve vector for {ElementType}", target.GetType().Name);
    }
  }

  private void TryAddFacetDisplayValue(BDE.Element target, List<Base> displayValue)
  {
    if (TryAddSolidPrimitiveFacetDisplayValue(target, displayValue))
    {
      return;
    }

    var facets = new FacetCollectorProcessor();
    try
    {
      ElementGraphicsOutput.Process(target, facets);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to process display facets for {ElementType}", target.GetType().Name);
      return;
    }

    foreach (PolyfaceHeader polyface in facets.Meshes)
    {
      try
      {
        displayValue.Add(meshConverter.Convert(polyface));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogDebug(ex, "Failed to convert display facet mesh for {ElementType}", target.GetType().Name);
      }
    }
  }

  private bool TryAddSolidPrimitiveFacetDisplayValue(BDE.Element target, List<Base> displayValue)
  {
    if (target is not BDE.SolidElement solidElement)
    {
      return false;
    }

    try
    {
      SolidPrimitive primitive = solidElement.GetSolidPrimitive();
      var options = new FacetOptions
      {
        CombineFacets = true,
        NormalsRequired = false,
        ParamsRequired = false,
        MaxEdgeLength = _facetMaxEdgeLength,
        AngleTolerance = _facetAngleTolerance,
        ChordTolerance = _facetChordTolerance,
      };

      using var construction = new PolyfaceConstruction(options);
      if (!construction.AddSolidPrimitive(primitive))
      {
        return false;
      }

      PolyfaceHeader mesh = construction.GetClientMesh();
      displayValue.Add(meshConverter.Convert(mesh));
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to extract mesh from SolidPrimitive for {ElementType}", target.GetType().Name);
      return false;
    }
  }

  private IEnumerable<BDE.Element> EnumerateChildren(BDE.Element element)
  {
    var seen = new HashSet<string>(StringComparer.Ordinal);

    IEnumerable<object?> children;
    try
    {
      children = element.GetChildren().Cast<object?>();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to enumerate child elements for {ElementType}", element.GetType().Name);
      children = Array.Empty<object?>();
    }

    foreach (var child in children)
    {
      if (child is BDE.Element childElement && !childElement.IsInvisible)
      {
        string key = childElement.ElementId.ToString();
        if (seen.Add(key))
        {
          yield return childElement;
        }
      }
    }

    foreach (BDE.Element child in EnumerateChildrenViaChildIterator(element))
    {
      if (!child.IsInvisible)
      {
        string key = child.ElementId.ToString();
        if (seen.Add(key))
        {
          yield return child;
        }
      }
    }
  }

  private IEnumerable<BDE.Element> EnumerateChildrenViaChildIterator(BDE.Element element)
  {
    var children = new List<BDE.Element>();

    try
    {
      ReflectionAssembly assembly = element.GetType().Assembly;
      Type? reasonType = assembly.GetType("Bentley.DgnPlatformNET.Elements.ExposeChildrenReason");
      Type? iteratorType = assembly.GetType("Bentley.DgnPlatformNET.Elements.ChildElemIter");
      if (reasonType is null || iteratorType is null)
      {
        return children;
      }

      ReflectionConstructorInfo? reasonConstructor = reasonType.GetConstructor(new[] { typeof(int) });
      if (reasonConstructor is null)
      {
        return children;
      }

      object reason = reasonConstructor.Invoke(new object[] { 100 });

      ReflectionConstructorInfo? iteratorConstructor = iteratorType.GetConstructor(new[] { typeof(BDE.Element), reasonType });
      if (iteratorConstructor is null)
      {
        return children;
      }

      ReflectionMethodInfo? isValidMethod = iteratorType.GetMethod(
        "IsValid",
        ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance
      );
      ReflectionMethodInfo? getElementMethod = iteratorType.GetMethod(
        "GetElement",
        ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance
      );
      ReflectionMethodInfo? toNextMethod = iteratorType.GetMethod(
        "ToNext",
        ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance
      );
      if (isValidMethod is null || getElementMethod is null || toNextMethod is null)
      {
        return children;
      }

      object? iterator = iteratorConstructor.Invoke(new object[] { element, reason });
      while (iterator is not null)
      {
        object? isValid = isValidMethod.Invoke(iterator, null);
        if (isValid is not bool valid || !valid)
        {
          break;
        }

        if (getElementMethod.Invoke(iterator, null) is BDE.Element child)
        {
          children.Add(child);
        }

        iterator = toNextMethod.Invoke(iterator, null);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "ChildElemIter traversal failed for {ElementType}", element.GetType().Name);
    }

    return children;
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
      Meshes.Add(meshData);
      return BentleyStatus.Success;
    }

    public override BentleyStatus ProcessCurveVector(CurveVector vector, bool isFilled) => BentleyStatus.Success;

    public override BentleyStatus ProcessCurvePrimitive(CurvePrimitive curvePrimitive, bool isClosed, bool isFilled) =>
      BentleyStatus.Success;
  }

  private sealed class SurfaceCollectorProcessor : ElementGraphicsProcessor
  {
    public List<MSBsplineSurface> Surfaces { get; } = new();

    public override bool ProcessAsBody(bool isCurved) => true;

    public override bool ProcessAsFacets(bool isPolyface) => false;

    public override bool WantClipping() => false;

    public override BentleyStatus ProcessSurface(MSBsplineSurface surface)
    {
      Surfaces.Add(surface);
      return BentleyStatus.Success;
    }

    public override BentleyStatus ProcessFacets(PolyfaceHeader meshData, bool filled) => BentleyStatus.Success;

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

  private static bool IsParametricSolidType(string elementTypeName) => elementTypeName is "Solid" or "Cone";

  private SOG.Surface ConvertSurface(BG.MSBsplineSurface target)
  {
    uint uCount = (uint)target.UPoleCount;
    uint vCount = (uint)target.VPoleCount;

    var points = new List<List<SOG.ControlPoint>>((int)uCount);
    for (uint u = 0; u < uCount; u++)
    {
      var row = new List<SOG.ControlPoint>((int)vCount);
      for (uint v = 0; v < vCount; v++)
      {
        uint index = u * vCount + v;
        BG.DPoint3d pole = target.get_PoleAt(index);
        SOG.Point point = pointConverter.Convert(pole);

        double weight = target.IsRational ? target.get_WeightAt(index) : 1.0;
        row.Add(new SOG.ControlPoint(point.x, point.y, point.z, weight, settingsStore.Current.SpeckleUnits));
      }

      points.Add(row);
    }

    return new SOG.Surface(points)
    {
      degreeU = target.UOrder - 1,
      degreeV = target.VOrder - 1,
      rational = target.IsRational,
      closedU = target.IsUClosed,
      closedV = target.IsVClosed,
      domainU = new SOP.Interval { start = target.UKnotRange.Low, end = target.UKnotRange.High },
      domainV = new SOP.Interval { start = target.VKnotRange.Low, end = target.VKnotRange.High },
      knotsU = Enumerable.Range(0, target.UKnotCount).Select(i => target.get_UKnotAt((uint)i)).ToList(),
      knotsV = Enumerable.Range(0, target.VKnotCount).Select(i => target.get_VKnotAt((uint)i)).ToList(),
      units = settingsStore.Current.SpeckleUnits,
    };
  }

  private bool TryGetSurfaceGeometry(BDE.SurfaceElement target, out SOG.Surface? surface)
  {
    var collector = new SurfaceCollectorProcessor();

    try
    {
      ElementGraphicsOutput.Process(target, collector);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to process display surfaces for {ElementType}", target.GetType().Name);
      surface = null;
      return false;
    }

    if (collector.Surfaces.Count == 0)
    {
      surface = null;
      return false;
    }

    try
    {
      surface = ConvertSurface(collector.Surfaces[0]);
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to convert display surface for {ElementType}", target.GetType().Name);
      surface = null;
      return false;
    }
  }

  #pragma warning disable IDE0002
  private Dictionary<string, object?> ExtractPrimitiveValues(BDE.SolidElement target)
  {
    var values = new Dictionary<string, object?>();

    try
    {
      SolidPrimitive primitive = target.GetSolidPrimitive();
      values["primitiveType"] = primitive.PrimitiveType.ToString();

      object details = primitive.PrimitiveType switch
      {
        SolidPrimitiveType.DgnBox => primitive.TryGetDgnBoxDetail(),
        SolidPrimitiveType.DgnCone => primitive.TryGetDgnConeDetail(),
        SolidPrimitiveType.DgnExtrusion => primitive.TryGetDgnExtrusionDetail(),
        SolidPrimitiveType.DgnRotationalSweep => primitive.TryGetDgnRotationalSweepDetail(),
        SolidPrimitiveType.DgnRuledSweep => primitive.TryGetDgnRuledSweepDetail(),
        SolidPrimitiveType.DgnSphere => primitive.TryGetDgnSphereDetail(),
        SolidPrimitiveType.DgnTorusPipe => primitive.TryGetDgnTorusPipeDetail(),
        _ => primitive,
      };

      foreach (var property in details.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
      {
        object? value = property.GetValue(details);
        values[property.Name] = value?.ToString();
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to extract solid primitive values for {ElementType}", target.GetType().Name);
    }

    return values;
  }
#pragma warning restore IDE0002

  private List<SOG.Mesh> CollectFacetMeshesFromElementAndChildren(BDE.Element element)
  {
    var meshes = new List<SOG.Mesh>();
    CollectFacetMeshesRecursive(element, meshes);
    return meshes;
  }

  private void CollectFacetMeshesRecursive(BDE.Element element, List<SOG.Mesh> meshes)
  {
    var displayValue = new List<Base>();
    TryAddFacetDisplayValue(element, displayValue);

    meshes.AddRange(displayValue.OfType<SOG.Mesh>());

    foreach (BDE.Element child in EnumerateChildren(element))
    {
      CollectFacetMeshesRecursive(child, meshes);
    }
  }

  private static bool TryExtractGraphicObject(DataObject dataObject, out Base? graphicObject)
  {
    if (dataObject.displayValue.Count == 1)
    {
      graphicObject = dataObject.displayValue[0];
      return true;
    }

    if (TryMergeMeshDisplayValues(dataObject.displayValue, out SOG.Mesh? mergedMesh))
    {
      graphicObject = mergedMesh;
      return true;
    }

    graphicObject = null;
    return false;
  }

  private static bool TryMergeMeshDisplayValues(IReadOnlyList<Base> displayValue, out SOG.Mesh? mergedMesh)
  {
    if (displayValue.Count == 0 || displayValue.Any(x => x is not SOG.Mesh))
    {
      mergedMesh = null;
      return false;
    }

    if (displayValue.Count == 1)
    {
      mergedMesh = (SOG.Mesh)displayValue[0];
      return true;
    }

    var mergedVertices = new List<double>();
    var mergedFaces = new List<int>();
    string units = ((SOG.Mesh)displayValue[0]).units;

    foreach (SOG.Mesh mesh in displayValue.Cast<SOG.Mesh>())
    {
      int vertexOffset = mergedVertices.Count / 3;
      mergedVertices.AddRange(mesh.vertices);
      AppendFaces(mesh.faces, mergedFaces, vertexOffset);
    }

    mergedMesh = new SOG.Mesh { vertices = mergedVertices, faces = mergedFaces, units = units };
    return true;
  }

  private static void AppendFaces(IReadOnlyList<int> sourceFaces, IList<int> targetFaces, int vertexOffset)
  {
    int index = 0;
    while (index < sourceFaces.Count)
    {
      int facePrefix = sourceFaces[index++];
      targetFaces.Add(facePrefix);

      int vertexCount = facePrefix switch
      {
        0 => 3,
        1 => 4,
        _ => Math.Abs(facePrefix),
      };

      for (int i = 0; i < vertexCount && index < sourceFaces.Count; i++)
      {
        targetFaces.Add(sourceFaces[index++] + vertexOffset);
      }
    }
  }

  private static double ResolveToleranceEnvironmentValue(string key, double fallback)
  {
    string? raw = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(raw))
    {
      return fallback;
    }

    return double.TryParse(raw, out double value) && value >= 0 ? value : fallback;
  }
}
