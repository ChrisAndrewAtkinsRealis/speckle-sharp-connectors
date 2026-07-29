using NUnit.Framework;
using Speckle.Converters.MicroStation;
using BG = Bentley.GeometryNET;

namespace Speckle.Converters.MicroStationShared.Tests;

[TestFixture]
public class ReferencePointConverterTests
{
  [Test]
  public void FirstConvertedPoint_BecomesOrigin_AndIsReturnedAsZero()
  {
    var converter = new ReferencePointConverter();

    BG.DPoint3d result = converter.ConvertToExternalCoordinates(new BG.DPoint3d(538_241.5, 2_847_112.25, 12.4));

    Assert.That(result.X, Is.EqualTo(0));
    Assert.That(result.Y, Is.EqualTo(0));
    Assert.That(result.Z, Is.EqualTo(0));
    Assert.That(converter.Origin, Is.Not.Null);
    Assert.That(converter.Origin!.Value.X, Is.EqualTo(538_241.5));
  }

  [Test]
  public void SubsequentPoints_AreRecenteredRelativeToTheFirstPoint()
  {
    var converter = new ReferencePointConverter();

    converter.ConvertToExternalCoordinates(new BG.DPoint3d(500_000, 200_000, 10));
    BG.DPoint3d result = converter.ConvertToExternalCoordinates(new BG.DPoint3d(500_010, 200_005, 12));

    Assert.That(result.X, Is.EqualTo(10).Within(1e-9));
    Assert.That(result.Y, Is.EqualTo(5).Within(1e-9));
    Assert.That(result.Z, Is.EqualTo(2).Within(1e-9));
  }

  [Test]
  public void ConvertFromExternalCoordinates_ReversesTheOffset()
  {
    var converter = new ReferencePointConverter();

    converter.ConvertToExternalCoordinates(new BG.DPoint3d(500_000, 200_000, 10));
    BG.DPoint3d recentered = converter.ConvertToExternalCoordinates(new BG.DPoint3d(500_010, 200_005, 12));
    BG.DPoint3d restored = converter.ConvertFromExternalCoordinates(recentered);

    Assert.That(restored.X, Is.EqualTo(500_010).Within(1e-6));
    Assert.That(restored.Y, Is.EqualTo(200_005).Within(1e-6));
    Assert.That(restored.Z, Is.EqualTo(12).Within(1e-6));
  }

  [Test]
  public void ConvertFromExternalCoordinates_IsNoOp_WhenNoOriginEstablished()
  {
    var converter = new ReferencePointConverter();

    BG.DPoint3d point = new(1, 2, 3);
    BG.DPoint3d result = converter.ConvertFromExternalCoordinates(point);

    Assert.That(result.X, Is.EqualTo(1));
    Assert.That(result.Y, Is.EqualTo(2));
    Assert.That(result.Z, Is.EqualTo(3));
  }

  [Test]
  public void SetOrigin_ExplicitlySeedsTheOrigin_InsteadOfLazyCapture()
  {
    var converter = new ReferencePointConverter();

    converter.SetOrigin(new BG.DPoint3d(500_000, 200_000, 10));
    BG.DPoint3d result = converter.ConvertToExternalCoordinates(new BG.DPoint3d(500_010, 200_005, 12));

    Assert.That(result.X, Is.EqualTo(10).Within(1e-9));
    Assert.That(result.Y, Is.EqualTo(5).Within(1e-9));
    Assert.That(result.Z, Is.EqualTo(2).Within(1e-9));
  }
}
