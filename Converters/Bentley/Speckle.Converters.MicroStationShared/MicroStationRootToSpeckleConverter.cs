using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation;

public class MicroStationRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _settingsStore;
  private readonly PropertiesExtractor _propertiesExtractor;

  public MicroStationRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
  }

  public Base Convert(object target)
  {
    if (target is not BDE.Element element)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from Element are."
      );
    }

    Type type = element.GetType();
    var objectConverter = _toSpeckle.ResolveConverter(type);
    Base rawGeometry = objectConverter.Convert(element);

    // cells already produce a wrapped data object with their children as display value
    if (rawGeometry is DataObject dataObject)
    {
      var cellProperties = _propertiesExtractor.GetProperties(element);
      foreach (var kvp in cellProperties)
      {
        dataObject.properties[kvp.Key] = kvp.Value;
      }
      return dataObject;
    }

    string typeName = type.Name;
    return new DataObject
    {
      name = typeName,
      displayValue = [rawGeometry],
      properties = _propertiesExtractor.GetProperties(element),
      ["type"] = typeName,
      ["units"] = _settingsStore.Current.SpeckleUnits,
    };
  }
}
