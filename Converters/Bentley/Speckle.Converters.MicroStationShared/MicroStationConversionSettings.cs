namespace Speckle.Converters.MicroStation;

/// <summary>
/// Conversion settings for a single send/receive operation.
/// </summary>
/// <param name="File">The active DGN file.</param>
/// <param name="Model">The active DGN model.</param>
/// <param name="UorPerMaster">Units of resolution per master unit. All native coordinates are in UoRs and must be divided by this factor to get master units.</param>
/// <param name="SpeckleUnits">The Speckle units string corresponding to the model's master unit.</param>
public record MicroStationConversionSettings(
  BDPN.DgnFile File,
  BDPN.DgnModel Model,
  double UorPerMaster,
  string SpeckleUnits
);
