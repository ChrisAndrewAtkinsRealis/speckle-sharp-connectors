using System.Collections.Generic;
using NUnit.Framework;
using Speckle.Connectors.MicroStation.HostApp;

namespace Speckle.Connectors.MicroStationShared.Tests;

// The test project references the MicroStation connector project directly to exercise the shared HostApp sources.

[TestFixture]
public class MicroStationItemTypeBakerTests
{
  [Test]
  public void TryParseItemTypeEntry_ReturnsLibraryAndValues_ForWellFormedEntry()
  {
    var entry = new Dictionary<string, object?>
    {
      ["library"] = "Furniture",
      ["properties"] = new Dictionary<string, object?> { ["Type"] = "Sofa", ["Width"] = 1.2 },
    };

    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry(
      "Sofa",
      entry,
      out string library,
      out IDictionary<string, object?> values
    );

    Assert.That(result, Is.True);
    Assert.That(library, Is.EqualTo("Furniture"));
    Assert.That(values["Type"], Is.EqualTo("Sofa"));
    Assert.That(values["Width"], Is.EqualTo(1.2));
  }

  [Test]
  public void TryParseItemTypeEntry_ReturnsFalse_WhenItemTypeNameIsMissing()
  {
    var entry = new Dictionary<string, object?>
    {
      ["library"] = "Furniture",
      ["properties"] = new Dictionary<string, object?>(),
    };

    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry("", entry, out _, out _);

    Assert.That(result, Is.False);
  }

  [Test]
  public void TryParseItemTypeEntry_ReturnsFalse_WhenEntryIsNotADictionary()
  {
    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry("Sofa", "not a dictionary", out _, out _);

    Assert.That(result, Is.False);
  }

  [Test]
  public void TryParseItemTypeEntry_ReturnsFalse_WhenLibraryIsMissing()
  {
    var entry = new Dictionary<string, object?> { ["properties"] = new Dictionary<string, object?>() };

    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry("Sofa", entry, out _, out _);

    Assert.That(result, Is.False);
  }

  [Test]
  public void TryParseItemTypeEntry_ReturnsFalse_WhenLibraryIsBlank()
  {
    var entry = new Dictionary<string, object?>
    {
      ["library"] = "   ",
      ["properties"] = new Dictionary<string, object?>(),
    };

    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry("Sofa", entry, out _, out _);

    Assert.That(result, Is.False);
  }

  [Test]
  public void TryParseItemTypeEntry_ReturnsFalse_WhenPropertiesAreMissing()
  {
    var entry = new Dictionary<string, object?> { ["library"] = "Furniture" };

    bool result = MicroStationItemTypeBaker.TryParseItemTypeEntry("Sofa", entry, out _, out _);

    Assert.That(result, Is.False);
  }
}
