using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Applies received <see cref="ColorProxy"/> colours to baked elements on receive. Scoped per receive
/// operation.
/// </summary>
/// <remarks>
/// Colour is written via <c>Element.AddRgbColorAttribute(byte, byte, byte)</c>, invoked reflectively (the
/// proven ATRL/Atom pattern) so the connector still builds where that overload isn't present.
/// </remarks>
public class MicroStationColorBaker
{
  private readonly ILogger<MicroStationColorBaker> _logger;

  /// <summary>Object application id -> ARGB.</summary>
  private readonly Dictionary<string, int> _objectColors = new();

  public MicroStationColorBaker(ILogger<MicroStationColorBaker> logger)
  {
    _logger = logger;
  }

  public void ParseColors(IReadOnlyCollection<ColorProxy>? colorProxies)
  {
    if (colorProxies is null)
    {
      return;
    }

    foreach (ColorProxy proxy in colorProxies)
    {
      foreach (string objectId in proxy.objects)
      {
        _objectColors[objectId] = proxy.value;
      }
    }
  }

  public void ApplyColor(BDE.Element element, string objectId)
  {
    if (!_objectColors.TryGetValue(objectId, out int argb))
    {
      return;
    }

    try
    {
      byte r = (byte)((argb >> 16) & 0xFF);
      byte g = (byte)((argb >> 8) & 0xFF);
      byte b = (byte)(argb & 0xFF);

      var method = element
        .GetType()
        .GetMethod("AddRgbColorAttribute", new[] { typeof(byte), typeof(byte), typeof(byte) });
      method?.Invoke(element, new object[] { r, g, b });
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to apply colour to element {ObjectId}", objectId);
    }
  }
}
