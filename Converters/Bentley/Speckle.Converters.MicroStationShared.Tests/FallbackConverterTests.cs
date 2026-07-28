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
      new FakeLogger<ElementToSpeckleFallbackConverter>()
    );

    var dataObject = converter.Convert(new FakeElement());

    Assert.That(dataObject, Is.TypeOf<DataObject>());
    Assert.That(dataObject.displayValue, Is.Empty);
    Assert.That(dataObject.properties["conversionKind"], Is.EqualTo("fallback"));
  }

  private sealed class FakeElement : BDE.Element
  {
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
