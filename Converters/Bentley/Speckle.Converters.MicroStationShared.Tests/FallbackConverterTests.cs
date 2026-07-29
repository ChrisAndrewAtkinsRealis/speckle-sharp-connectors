using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation;
using Speckle.Converters.MicroStation.ToSpeckle.TopLevel;
using Speckle.Objects.Data;
using BDE = Bentley.DgnPlatformNET.Elements;
using BG = Bentley.GeometryNET;
using SOG = Speckle.Objects.Geometry;
using SOP = Speckle.Objects.Primitive;

namespace Speckle.Converters.MicroStationShared.Tests;

// The test project references the MicroStation converter project directly so it can exercise the shared converter sources.

[TestFixture]
public class FallbackConverterTests
{
  [Test]
  public void FallbackConverter_ReturnsPropertiesOnlyDataObject_WhenNoDisplayGeometryIsExtracted()
  {
    var converter = new ElementToSpeckleFallbackConverter(
      new FakeMeshConverter(),
      new FakeCurveVectorConverter(),
      new FakeSettingsStore(),
      new FakeLogger<ElementToSpeckleFallbackConverter>(),
      new FakeReferencePointConverter()
    );

    var dataObject = converter.Convert(new FakeElement());

    Assert.That(dataObject, Is.TypeOf<DataObject>());
    Assert.That(dataObject.displayValue, Is.Empty);
    Assert.That(dataObject.properties["conversionKind"], Is.EqualTo("fallback"));
  }

  [Test]
  public void FallbackConverter_ReturnsBoundingBoxMesh_WhenOnlyElementRangeIsAvailable()
  {
    var converter = new ElementToSpeckleFallbackConverter(
      new FakeMeshConverter(),
      new FakeCurveVectorConverter(),
      new FakeSettingsStore(),
      new FakeLogger<ElementToSpeckleFallbackConverter>(),
      new FakeReferencePointConverter()
    );

    var result = converter.Convert(new FakeElementWithRange());

    // no curve/solid/facet/child geometry was extractable, so this is the last-resort
    // bounding-box mesh (Converters/Bentley/PLAN.md §7) rather than the element's real shape.
    Assert.That(result, Is.TypeOf<SOG.Mesh>());
    var mesh = (SOG.Mesh)result;
    Assert.That(mesh.vertices, Has.Count.EqualTo(24));
    Assert.That(mesh.vertices[0], Is.EqualTo(0));
    Assert.That(mesh.vertices[18], Is.EqualTo(1));
    Assert.That(mesh.vertices[19], Is.EqualTo(2));
    Assert.That(mesh.vertices[20], Is.EqualTo(3));
  }

  private sealed class FakeElement : BDE.Element
  {
  }

  private sealed class FakeElementWithRange : BDE.Element
  {
    public FakeRange GetTestElementRange() => new() { Low = new FakePoint(0, 0, 0), High = new FakePoint(1, 2, 3) };
  }

  private sealed class FakeRange
  {
    public FakePoint Low { get; set; }
    public FakePoint High { get; set; }
  }

  private readonly struct FakePoint(double x, double y, double z)
  {
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Z { get; } = z;
  }

  private sealed class FakeMeshConverter : ITypedConverter<BG.PolyfaceHeader, SOG.Mesh>
  {
    public SOG.Mesh Convert(BG.PolyfaceHeader target) => new();
  }

  private sealed class FakeCurveVectorConverter : ITypedConverter<BG.CurveVector, List<ICurve>>
  {
    public List<ICurve> Convert(BG.CurveVector target) => [];
  }

  private sealed class FakeSettingsStore : IConverterSettingsStore<MicroStationConversionSettings>
  {
    public MicroStationConversionSettings Current { get; } = new();

    public IDisposable Push(Func<MicroStationConversionSettings, MicroStationConversionSettings> nextContext) =>
      new NoopDisposable();

    public void Initialize(MicroStationConversionSettings context) { }
  }

  private sealed class NoopDisposable : IDisposable
  {
    public void Dispose() { }
  }

  // passthrough: these tests assert absolute box-corner values, so they don't exercise recentering behavior
  // (covered separately by the raw geometry converters that use the real ReferencePointConverter).
  private sealed class FakeReferencePointConverter : IReferencePointConverter
  {
    public BG.DPoint3d? Origin => null;

    public void SetOrigin(BG.DPoint3d origin) { }

    public BG.DPoint3d ConvertToExternalCoordinates(BG.DPoint3d point) => point;

    public BG.DPoint3d ConvertFromExternalCoordinates(BG.DPoint3d point) => point;
  }

  private sealed class FakeLogger<T> : ILogger<T>
  {
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
  }

  private sealed class NullScope : IDisposable
  {
    public static NullScope Instance { get; } = new();

    public void Dispose() { }
  }
}
