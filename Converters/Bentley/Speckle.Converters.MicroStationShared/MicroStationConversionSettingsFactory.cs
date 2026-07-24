using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.MicroStation;

[GenerateAutoInterface]
public class MicroStationConversionSettingsFactory(IHostToSpeckleUnitConverter<string> unitsConverter)
  : IMicroStationConversionSettingsFactory
{
  public MicroStationConversionSettings Create(BDPN.DgnFile file, BDPN.DgnModel model)
  {
    var modelInfo = model.GetModelInfo();

    // e.g. "Meter", "Millimeter", "Foot" - the (singular, non-abbreviated) master unit name
    string masterUnitName = modelInfo.GetMasterUnit().GetName(true, true);

    // Model identity: a DGN file contains many models (design 2D/3D, drawing, sheet). We record the active
    // model's name, type and dimensionality so the source structure is preserved on the root collection.
    // NOTE: ModelName/Is3D/ModelType are Bentley-API-dependent reads; ModelType is emitted as its raw enum
    // string to avoid depending on specific enum member names.
    string modelName = model.ModelName ?? "Default";
    bool is3d = modelInfo.Is3d;
    string modelType = model.ModelType.ToString();

    return new(
      file,
      model,
      modelInfo.UorPerMaster,
      unitsConverter.ConvertOrThrow(masterUnitName),
      modelName,
      modelType,
      is3d
    );
  }
}
