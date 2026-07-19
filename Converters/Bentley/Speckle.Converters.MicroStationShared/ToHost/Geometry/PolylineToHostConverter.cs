using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToHost.Geometry;

/// <summary>
/// Open polylines are baked as line strings; closed polylines as shape elements.
/// </summary>
[NameAndRankValue(typeof(SOG.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToHostConverter(
  MicroStationUnitScaler scaler,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore
) : IToHostTopLevelConverter, ITypedConverter<SOG.Polyline, BDE.DisplayableElement>
{
  public object Convert(Base target) => Convert((SOG.Polyline)target);

  public BDE.DisplayableElement Convert(SOG.Polyline target)
  {
    var points = scaler.PointListToNative(target.value, target.units).ToList();

    if (target.closed)
    {
      if (points.Count > 0 && !points[0].IsEqual(points[^1], 1e-9))
      {
        points.Add(points[0]);
      }

      return new BDE.ShapeElement(settingsStore.Current.Model, null, points.ToArray());
    }

    return new BDE.LineStringElement(settingsStore.Current.Model, null, points.ToArray());
  }
}
