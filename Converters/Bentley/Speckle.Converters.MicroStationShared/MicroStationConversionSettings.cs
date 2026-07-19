namespace Speckle.Converters.MicroStation;

/// <summary>
/// Conversion settings for a single send/receive operation, scoped to the active model.
/// </summary>
/// <param name="File">The active DGN file (a file can contain many models).</param>
/// <param name="Model">The active DGN model.</param>
/// <param name="UorPerMaster">Units of resolution per master unit. All native coordinates are in UoRs and must be divided by this factor to get master units.</param>
/// <param name="SpeckleUnits">The Speckle units string corresponding to the model's master unit.</param>
/// <param name="ModelName">The active model's name.</param>
/// <param name="ModelType">The active model's type (e.g. Design/Drawing/Sheet), as reported by the host.</param>
/// <param name="Is3d">Whether the active model is 3D (false for a 2D design model).</param>
public record MicroStationConversionSettings(
  BDPN.DgnFile File,
  BDPN.DgnModel Model,
  double UorPerMaster,
  string SpeckleUnits,
  string ModelName,
  string ModelType,
  bool Is3d
);
