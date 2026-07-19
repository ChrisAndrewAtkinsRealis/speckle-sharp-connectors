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

    return new(file, model, modelInfo.UorPerMaster, unitsConverter.ConvertOrThrow(masterUnitName));
  }
}
