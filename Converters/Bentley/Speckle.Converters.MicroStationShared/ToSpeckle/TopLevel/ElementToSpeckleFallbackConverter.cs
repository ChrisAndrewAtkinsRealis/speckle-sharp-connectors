using System.Reflection;
using System.Text;
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

[NameAndRankValue(typeof(BDE.Element), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK - 100)]
public class ElementToSpeckleFallbackConverter(
  ITypedConverter<BG.PolyfaceHeader, SOG.Mesh> meshConverter,
  ITypedConverter<BG.CurveVector, List<ICurve>> curveVectorConverter,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  ILogger<ElementToSpeckleFallbackConverter> logger,
  IReferencePointConverter referencePointConverter
) : IToSpeckleTopLevelConverter
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

  public Base Convert(object target) => Convert((BDE.Element)target);

  public Base Convert(BDE.Element target)
  {
    var displayValue = new List<Base>();

    // try direct curve extraction first for curve-bearing elements
    var curveVector = target.GetCurveVectorOrNull();
    if (curveVector is not null)
    {
      displayValue.AddRange(curveVectorConverter.Convert(curveVector).Cast<Base>());
    }

    // fast-path solid primitive faceting for solid-like unsupported elements
    TryAddQueriedSolidPrimitiveMesh(target, displayValue);
    TryAddSolidPrimitiveMesh(target, displayValue);

    // then collect facet meshes from graphics processing (covers most displayable solids/surfaces/extended elements)
    var facets = new FacetCollectorProcessor();
    try
    {
      ElementGraphicsOutput.Process(target, facets);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed to process display facets for {ElementType}", target.GetType().Name);
    }

    foreach (PolyfaceHeader polyface in facets.Meshes)
    {
      try
      {
        displayValue.Add(meshConverter.Convert(polyface));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to convert fallback facet mesh for {ElementType}", target.GetType().Name);
      }
    }

    if (displayValue.Count == 0)
    {
      try
      {
        foreach (BDE.Element child in EnumerateChildren(target, includeInvisible: target is BDE.ExtendedElementElement))
        {
          var childDisplayValue = new List<Base>();
          if (TryCollectDisplayValue(child, childDisplayValue))
          {
            displayValue.AddRange(childDisplayValue);
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to enumerate fallback children for {ElementType}", target.GetType().Name);
        WriteBootstrapLog("WARN", $"Failed to enumerate fallback children for {target.GetType().Name}", ex);
      }
    }

    if (displayValue.Count == 0 && TryAddRangeFallbackMesh(target, displayValue))
    {
      WriteBootstrapLog("INFO", $"Range fallback mesh generated for {target.GetType().Name} ({target.ElementType}).", null);
    }

    string typeName = target.GetType().Name;
    string elementTypeName = target.ElementType.ToString();

    var properties = new Dictionary<string, object?>
    {
      ["sourceElementClass"] = typeName,
      ["sourceMSElementType"] = elementTypeName,
      ["sourceMSElementTypeValue"] = (int)target.ElementType,
      ["isExtendedElementType"] = IsExtendedElementType(elementTypeName),
      ["isParametricSolidType"] = IsParametricSolidType(elementTypeName),
      ["conversionKind"] = "fallback",
    };

    Base result;

    if (displayValue.Count == 0)
    {
      result = new DataObject
      {
        name = typeName,
        displayValue = [],
        properties = properties,
        ["type"] = typeName,
        ["units"] = settingsStore.Current.SpeckleUnits,
      };

      LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, 0, result.GetType().Name);
      return result;
    }

    var meshes = displayValue.OfType<SOG.Mesh>().ToList();
    if (IsParametricSolidCandidate(typeName, elementTypeName) && meshes.Count > 0)
    {
      try
      {
        var solid = new SOG.SolidX
        {
          displayValue = meshes,
          encodedValue = new RawEncoding { format = "microstation", contents = string.Empty },
          units = settingsStore.Current.SpeckleUnits,
        };

        solid["parametricValues"] = new DataObject
        {
          name = "MicroStation Parametric Values",
          displayValue = [],
          properties = properties,
          ["type"] = "MicroStation.ParametricValues",
          ["units"] = settingsStore.Current.SpeckleUnits,
        };

        result = solid;
        LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, meshes.Count, nameof(SOG.SolidX));
        return result;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to create SolidX fallback for {SourceType}; falling back to mesh/data", typeName);
      }
    }

    if (meshes.Count > 0 && TryMergeMeshDisplayValues(meshes.Cast<Base>().ToList(), out SOG.Mesh? mergedMeshesOnly))
    {
      result = mergedMeshesOnly!;
      LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, meshes.Count, nameof(SOG.Mesh));
      return result;
    }

    if (displayValue.Count == 1)
    {
      result = displayValue[0];
      LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, meshes.Count, result.GetType().Name);
      return result;
    }

    if (TryMergeMeshDisplayValues(displayValue, out SOG.Mesh? mergedMesh))
    {
      result = mergedMesh!;
      LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, meshes.Count, nameof(SOG.Mesh));
      return result;
    }

    result = new DataObject
    {
      name = typeName,
      displayValue = displayValue,
      properties = properties,
      ["type"] = typeName,
      ["units"] = settingsStore.Current.SpeckleUnits,
    };

    LogExtendedFallbackResult(typeName, elementTypeName, displayValue.Count, meshes.Count, result.GetType().Name);
    return result;
  }

  private bool TryCollectDisplayValue(BDE.Element target, List<Base> collectedDisplayValue)
  {
    var curveVector = target.GetCurveVectorOrNull();
    if (curveVector is not null)
    {
      collectedDisplayValue.AddRange(curveVectorConverter.Convert(curveVector).Cast<Base>());
      return collectedDisplayValue.Count > 0;
    }

    if (TryAddQueriedSolidPrimitiveMesh(target, collectedDisplayValue) || TryAddSolidPrimitiveMesh(target, collectedDisplayValue))
    {
      return true;
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
      try
      {
        collectedDisplayValue.Add(meshConverter.Convert(polyface));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to convert child fallback facet mesh for {ElementType}", target.GetType().Name);
      }
    }

    if (collectedDisplayValue.Count > 0)
    {
      return true;
    }

    bool collected = false;
    try
    {
      foreach (BDE.Element child in EnumerateChildren(target, includeInvisible: target is BDE.ExtendedElementElement))
      {
        if (TryCollectDisplayValue(child, collectedDisplayValue))
        {
          collected = true;
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed to recurse fallback children for {ElementType}", target.GetType().Name);
      WriteBootstrapLog("WARN", $"Failed to recurse fallback children for {target.GetType().Name}", ex);
    }

    return collected;
  }

  private IEnumerable<BDE.Element> EnumerateChildren(BDE.Element element, bool includeInvisible = false)
  {
    var seen = new HashSet<string>(StringComparer.Ordinal);
    bool foundViaGetChildren = false;

    foreach (var child in element.GetChildren())
    {
      if (child is BDE.Element childElement && (includeInvisible || !childElement.IsInvisible))
      {
        string key = childElement.ElementId.ToString();
        if (seen.Add(key))
        {
          foundViaGetChildren = true;
          yield return childElement;
        }
      }
    }

    // GetChildren() reports no children for element/file combinations where ChildElemIter still finds real
    // children (confirmed against a live file: ELEMENT_COVERAGE.csv/report.json show ChildCount_GetChildren
    // == -1 while ChildCount_ChildElemIter has real counts, for elements that aren't ExtendedElementElement).
    // Always fall back to the iterator when GetChildren() came up empty, not just for that one type.
    if (foundViaGetChildren && element is not BDE.ExtendedElementElement)
    {
      yield break;
    }

    foreach (BDE.Element child in EnumerateChildrenViaChildIterator(element, includeInvisible))
    {
      string key = child.ElementId.ToString();
      if (seen.Add(key))
      {
        yield return child;
      }
    }
  }

  private IEnumerable<BDE.Element> EnumerateChildrenViaChildIterator(BDE.Element element, bool includeInvisible)
  {
    var children = new List<BDE.Element>();

    try
    {
      Assembly assembly = element.GetType().Assembly;
      Type? reasonType = assembly.GetType("Bentley.DgnPlatformNET.Elements.ExposeChildrenReason");
      Type? iteratorType = assembly.GetType("Bentley.DgnPlatformNET.Elements.ChildElemIter");
      if (reasonType is null || iteratorType is null)
      {
        return children;
      }

      ConstructorInfo? reasonConstructor = reasonType.GetConstructor(new[] { typeof(int) });
      if (reasonConstructor is null)
      {
        return children;
      }

      object reason = reasonConstructor.Invoke([100]);

      ConstructorInfo? iteratorConstructor = iteratorType.GetConstructor(new[] { typeof(BDE.Element), reasonType });
      if (iteratorConstructor is null)
      {
        return children;
      }

      MethodInfo? isValidMethod = iteratorType.GetMethod("IsValid", BindingFlags.Public | BindingFlags.Instance);
      MethodInfo? getElementMethod = iteratorType.GetMethod("GetElement", BindingFlags.Public | BindingFlags.Instance);
      MethodInfo? toNextMethod = iteratorType.GetMethod("ToNext", BindingFlags.Public | BindingFlags.Instance);
      if (isValidMethod is null || getElementMethod is null || toNextMethod is null)
      {
        return children;
      }

      object? iterator = iteratorConstructor.Invoke([element, reason]);
      while (iterator is not null)
      {
        object? isValid = isValidMethod.Invoke(iterator, null);
        if (isValid is not bool valid || !valid)
        {
          break;
        }

        if (getElementMethod.Invoke(iterator, null) is BDE.Element child && (includeInvisible || !child.IsInvisible))
        {
          children.Add(child);
        }

        iterator = toNextMethod.Invoke(iterator, null);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "ChildElemIter traversal failed for {ElementType}", element.GetType().Name);
      WriteBootstrapLog("WARN", $"ChildElemIter traversal failed for {element.GetType().Name}", ex);
    }

    return children;
  }

  private bool TryAddSolidPrimitiveMesh(BDE.Element target, List<Base> displayValue)
  {
    if (target is not BDE.SolidElement solidElement)
    {
      return false;
    }

    try
    {
      return TryAddSolidPrimitiveObjectMesh(solidElement.GetSolidPrimitive(), displayValue, target.GetType().Name);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Failed solid primitive meshing in fallback for {ElementType}", target.GetType().Name);
      return false;
    }
  }

  private bool TryAddQueriedSolidPrimitiveMesh(BDE.Element target, List<Base> displayValue)
  {
    if (target is BDE.SolidElement)
    {
      return false;
    }

    try
    {
      Assembly assembly = target.GetType().Assembly;
      Type? queryType = assembly.GetType("Bentley.DgnPlatformNET.Elements.ISolidPrimitiveQuery");
      MethodInfo? elementToSolidPrimitive = queryType?.GetMethod(
        "ElementToSolidPrimitive",
        BindingFlags.Public | BindingFlags.Static
      );
      if (elementToSolidPrimitive is null)
      {
        return false;
      }

      object? primitive = elementToSolidPrimitive.Invoke(null, [target, false]);
      if (primitive is null)
      {
        return false;
      }

      bool added = TryAddSolidPrimitiveObjectMesh(primitive, displayValue, target.GetType().Name);
      if (added && target is BDE.ExtendedElementElement)
      {
        WriteBootstrapLog("INFO", $"ISolidPrimitiveQuery produced mesh for {target.GetType().Name} ({target.ElementType}).", null);
      }

      return added;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "ISolidPrimitiveQuery failed in fallback for {ElementType}", target.GetType().Name);
      return false;
    }
  }

  private bool TryAddSolidPrimitiveObjectMesh(object primitive, List<Base> displayValue, string sourceTypeName)
  {
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

    MethodInfo? addMethod = typeof(PolyfaceConstruction)
      .GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .FirstOrDefault(m =>
      {
        if (m.Name != "AddSolidPrimitive")
        {
          return false;
        }

        ParameterInfo[] parameters = m.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(primitive);
      });

    if (addMethod is null)
    {
      return false;
    }

    object? addedResult = addMethod.Invoke(construction, [primitive]);
    if (addedResult is not bool added || !added)
    {
      return false;
    }

    PolyfaceHeader mesh = construction.GetClientMesh();
    try
    {
      displayValue.Add(meshConverter.Convert(mesh));
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed to convert solid primitive mesh for {ElementType}", sourceTypeName);
      return false;
    }
  }

  private bool TryAddRangeFallbackMesh(BDE.Element target, List<Base> displayValue)
  {
    if (!TryGetElementRangeBounds(target, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ))
    {
      return false;
    }

    double dx = maxX - minX;
    double dy = maxY - minY;
    double dz = maxZ - minZ;
    if (dx <= 0 && dy <= 0 && dz <= 0)
    {
      return false;
    }

    const double EPSILON = 1e-6;
    if (dx <= 0)
    {
      maxX = minX + EPSILON;
    }

    if (dy <= 0)
    {
      maxY = minY + EPSILON;
    }

    if (dz <= 0)
    {
      maxZ = minZ + EPSILON;
    }

    var corners = new (double x, double y, double z)[]
    {
      (minX, minY, minZ),
      (maxX, minY, minZ),
      (maxX, maxY, minZ),
      (minX, maxY, minZ),
      (minX, minY, maxZ),
      (maxX, minY, maxZ),
      (maxX, maxY, maxZ),
      (minX, maxY, maxZ),
    };

    var vertices = new List<double>(corners.Length * 3);
    foreach (var (x, y, z) in corners)
    {
      var extPoint = referencePointConverter.ConvertToExternalCoordinates(new BG.DPoint3d(x, y, z));
      vertices.Add(extPoint.X);
      vertices.Add(extPoint.Y);
      vertices.Add(extPoint.Z);
    }

    var faces = new List<int>
    {
      1,
      0,
      1,
      2,
      3,
      1,
      4,
      5,
      6,
      7,
      1,
      0,
      1,
      5,
      4,
      1,
      1,
      2,
      6,
      5,
      1,
      2,
      3,
      7,
      6,
      1,
      3,
      0,
      4,
      7,
      1,
      0,
      3,
      2,
      1,
    };

    displayValue.Add(new SOG.Mesh { vertices = vertices, faces = faces, units = settingsStore.Current.SpeckleUnits });
    return true;
  }

  private static bool TryGetElementRangeBounds(
    BDE.Element target,
    out double minX,
    out double minY,
    out double minZ,
    out double maxX,
    out double maxY,
    out double maxZ
  )
  {
    minX = minY = minZ = maxX = maxY = maxZ = 0;

    MethodInfo[] methods = target
      .GetType()
      .GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Where(m => m.Name.IndexOf("Range", StringComparison.OrdinalIgnoreCase) >= 0)
      .ToArray();

    foreach (MethodInfo method in methods)
    {
      ParameterInfo[] parameters = method.GetParameters();
      if (parameters.Any(p => !p.IsOut && !p.ParameterType.IsByRef && !p.IsOptional))
      {
        continue;
      }

      var args = new object?[parameters.Length];
      for (int i = 0; i < parameters.Length; i++)
      {
        ParameterInfo parameter = parameters[i];
        if (parameter.ParameterType.IsByRef)
        {
          Type valueType = parameter.ParameterType.GetElementType()!;
          args[i] = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
        }
        else if (parameter.IsOptional)
        {
          args[i] = parameter.DefaultValue;
        }
      }

      try
      {
        object? returnValue = method.Invoke(target, args);

        if (TryExtractRangeBounds(returnValue, out minX, out minY, out minZ, out maxX, out maxY, out maxZ))
        {
          return true;
        }

        foreach (object? arg in args)
        {
          if (TryExtractRangeBounds(arg, out minX, out minY, out minZ, out maxX, out maxY, out maxZ))
          {
            return true;
          }
        }
      }
      catch (Exception ex) when (
        ex is TargetInvocationException
          or ArgumentException
          or MethodAccessException
          or InvalidOperationException
          or NotSupportedException
      )
      {
        // keep probing other candidate APIs
      }
    }

    return false;
  }

  private static bool TryExtractRangeBounds(
    object? range,
    out double minX,
    out double minY,
    out double minZ,
    out double maxX,
    out double maxY,
    out double maxZ
  )
  {
    minX = minY = minZ = maxX = maxY = maxZ = 0;
    if (range is null)
    {
      return false;
    }

    object? low = GetMemberValue(range, "Low") ?? GetMemberValue(range, "low");
    object? high = GetMemberValue(range, "High") ?? GetMemberValue(range, "high");
    if (low is null || high is null)
    {
      return false;
    }

    if (!TryGetPointCoordinates(low, out minX, out minY, out minZ) || !TryGetPointCoordinates(high, out maxX, out maxY, out maxZ))
    {
      return false;
    }

    return true;
  }

  private static bool TryGetPointCoordinates(object point, out double x, out double y, out double z)
  {
    x = y = z = 0;
    object? xValue = GetMemberValue(point, "X") ?? GetMemberValue(point, "x");
    object? yValue = GetMemberValue(point, "Y") ?? GetMemberValue(point, "y");
    object? zValue = GetMemberValue(point, "Z") ?? GetMemberValue(point, "z");

    if (xValue is null || yValue is null || zValue is null)
    {
      return false;
    }

    try
    {
      x = System.Convert.ToDouble(xValue);
      y = System.Convert.ToDouble(yValue);
      z = System.Convert.ToDouble(zValue);
      return true;
    }
    catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
    {
      return false;
    }
  }

  private static object? GetMemberValue(object target, string memberName)
  {
    Type type = target.GetType();
    PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
    if (property is not null)
    {
      return property.GetValue(target);
    }

    FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
    return field?.GetValue(target);
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

  private static bool IsParametricSolidCandidate(string sourceTypeName, string elementTypeName)
  {
    bool hasExtendedElementTypeName = sourceTypeName.IndexOf("ExtendedElement", StringComparison.Ordinal) >= 0;
    bool hasSolidElementTypeName = elementTypeName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0;

    return IsParametricSolidType(elementTypeName)
      || sourceTypeName == "ExtendedElementElement"
      || (hasExtendedElementTypeName && hasSolidElementTypeName);
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

  private void LogExtendedFallbackResult(
    string sourceTypeName,
    string elementTypeName,
    int displayValueCount,
    int meshCount,
    string outputTypeName
  )
  {
    if (sourceTypeName != "ExtendedElementElement")
    {
      return;
    }

    logger.LogInformation(
      "Extended fallback result. ElementType={ElementType}, OutputType={OutputType}, DisplayValues={DisplayValues}, Meshes={Meshes}",
      elementTypeName,
      outputTypeName,
      displayValueCount,
      meshCount
    );

    string message =
      $"Extended fallback result. ElementType={elementTypeName}, OutputType={outputTypeName}, DisplayValues={displayValueCount}, Meshes={meshCount}";
    WriteBootstrapLog("INFO", message, null);
  }

  private static void WriteBootstrapLog(string level, string message, Exception? ex)
  {
    try
    {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      string folder = Path.Combine(appData, "Speckle", "Logs", "MicroStation");
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, "SpeckleBootstrap.log");

      var details = new StringBuilder(message);
      if (ex is not null)
      {
        _ = details.AppendLine();
        _ = details.Append(ex.GetType().FullName);
        _ = details.Append(": ");
        _ = details.Append(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
          _ = details.AppendLine();
          _ = details.Append(ex.StackTrace);
        }
      }

      string logLine = $"[{DateTime.UtcNow:O}] [{level}] [FallbackConverter] {details}{Environment.NewLine}";
      File.AppendAllText(path, logLine);
      System.Diagnostics.Debug.WriteLine(logLine);
    }
    catch (IOException)
    {
      // never throw from diagnostics logging
    }
    catch (UnauthorizedAccessException)
    {
      // never throw from diagnostics logging
    }
    catch (ArgumentException)
    {
      // never throw from diagnostics logging
    }
    catch (NotSupportedException)
    {
      // never throw from diagnostics logging
    }
    catch (InvalidOperationException)
    {
      // never throw from diagnostics logging
    }
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
