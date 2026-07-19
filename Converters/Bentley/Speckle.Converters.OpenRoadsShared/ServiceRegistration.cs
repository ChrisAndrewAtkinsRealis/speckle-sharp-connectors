using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.MicroStation;

namespace Speckle.Converters.OpenRoads;

public static class ServiceRegistration
{
  /// <summary>
  /// Registers the OpenRoads/OpenRail converters. The civil converters (Alignment/Profile/Corridor/Feature)
  /// live in this shared project, which the OpenRoads/OpenRail version projects import <em>alongside</em> the
  /// MicroStation converter shared project — so both compile into a single assembly. That means
  /// <see cref="Speckle.Converters.MicroStation.ServiceRegistration.AddMicroStationConverters"/>'s
  /// executing-assembly scan already discovers the civil top-level converters and registers them into the
  /// same converter manager as the geometry converters.
  /// </summary>
  /// <remarks>
  /// The MicroStation root converter only handles native <c>Element</c>s, so civil entities
  /// (<c>CifNET</c> alignments/corridors/etc.) are resolved via the converter manager directly by the civil
  /// root object builder on the connector side.
  /// </remarks>
  public static void AddOpenRoadsConverters(this IServiceCollection serviceCollection) =>
    serviceCollection.AddMicroStationConverters();
}
