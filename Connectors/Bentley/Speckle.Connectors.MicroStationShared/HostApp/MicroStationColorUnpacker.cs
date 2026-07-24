using Bentley.DgnPlatformNET;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Collects element colours into <see cref="ColorProxy"/> objects on send, so received geometry keeps its
/// appearance in other apps. Scoped per send operation.
/// </summary>
/// <remarks>
/// Reading an element's RGB is the uncertain surface: MicroStation stores colour as a uint that is either an
/// index into the model's colour table or a packed TBGR true-colour. We read it via
/// <see cref="ElementPropertiesGetter"/> and resolve indices through the model colour map, all defensively —
/// an element whose colour can't be resolved simply gets no colour proxy.
/// </remarks>
public class MicroStationColorUnpacker
{
  private readonly ILogger<MicroStationColorUnpacker> _logger;
  private readonly Dictionary<int, ColorProxy> _colorProxies = new();

  public MicroStationColorUnpacker(ILogger<MicroStationColorUnpacker> logger)
  {
    _logger = logger;
  }

  public List<ColorProxy> UnpackColors(IReadOnlyList<MicroStationRootObject> objects)
  {
    foreach (var (element, applicationId) in objects)
    {
      try
      {
        if (TryGetArgb(element, out int argb))
        {
          AddObjectToColorProxy(applicationId, argb);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogDebug(ex, "Failed to unpack colour from element");
      }
    }

    return _colorProxies.Values.ToList();
  }

  private void AddObjectToColorProxy(string objectId, int argb)
  {
    if (_colorProxies.TryGetValue(argb, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
      return;
    }

    var newProxy = new ColorProxy
    {
      value = argb,
      applicationId = argb.ToString(),
      name = argb.ToString(),
      objects = new List<string> { objectId },
    };
    _colorProxies[argb] = newProxy;
  }

  private bool TryGetArgb(BDE.Element element, out int argb)
  {
    argb = 0;
    try
    {
      using BDPN.ElementPropertiesGetter elementPropertyGetter = new(element);
      int raw = (int)elementPropertyGetter.Color;

      // resolve the raw colour (index or TBGR) against the model colour map to RGB bytes
      DgnColorMap colorMap = DgnColorMap.GetForDisplay(element.DgnModel);
      var rgb = colorMap.GetTbgrColors()[raw];

      // Tbgr packs bytes as 0x00BBGGRR
      byte r = (byte)(rgb & 0xFF);
      byte g = (byte)((rgb >> 8) & 0xFF);
      byte b = (byte)((rgb >> 16) & 0xFF);

      argb = (255 << 24) | (r << 16) | (g << 8) | b;
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Could not resolve element colour to RGB");
      return false;
    }
  }
}
