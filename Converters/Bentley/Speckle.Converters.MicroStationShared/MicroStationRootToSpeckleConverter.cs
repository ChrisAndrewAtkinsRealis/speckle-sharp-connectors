using System.Text;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation;

public class MicroStationRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _settingsStore;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly ItemTypePropertiesExtractor _itemTypePropertiesExtractor;

  public MicroStationRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor,
    ItemTypePropertiesExtractor itemTypePropertiesExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
    _itemTypePropertiesExtractor = itemTypePropertiesExtractor;
  }

  public Base Convert(object target)
  {
    LogInfo($"RootToSpeckle conversion invoked. TargetType={target.GetType().FullName}.");
    try
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
        foreach (var kvp in _propertiesExtractor.GetProperties(element))
        {
          dataObject.properties[kvp.Key] = kvp.Value;
        }

        var itemTypes = _itemTypePropertiesExtractor.GetProperties(element);
        if (itemTypes.Count > 0)
        {
          dataObject.properties["Item Types"] = itemTypes;
        }

        LogInfo($"RootToSpeckle conversion completed using DataObject payload. ElementType={type.Name}.");        return dataObject;
      }

      string typeName = type.Name;
      var properties = _propertiesExtractor.GetProperties(element);

      foreach (var kvp in _propertiesExtractor.GetProperties(element))
      {
        properties[kvp.Key] = kvp.Value;
      }

      var itemTypesForNewObject = _itemTypePropertiesExtractor.GetProperties(element);
      if (itemTypesForNewObject.Count > 0)
      {
        properties["Item Types"] = itemTypesForNewObject;
      }

      LogInfo($"RootToSpeckle conversion completed with wrapped DataObject. ElementType={typeName}.");      return new DataObject
      {
        name = typeName,
        displayValue = [rawGeometry],
        properties = properties,
        ["type"] = typeName,
        ["units"] = _settingsStore.Current.SpeckleUnits,
      };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      LogError($"RootToSpeckle conversion failed. TargetType={target.GetType().FullName}.", ex);
      throw;
    }
  }

  private static void LogInfo(string message) => WriteBootstrapLog("INFO", message, null);

  private static void LogError(string message, Exception? ex) => WriteBootstrapLog("ERROR", message, ex);

  private static void WriteBootstrapLog(string level, string message, Exception? ex)
  {
    try
    {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      string folder = Path.Combine(appData, "Speckle", "Logs", "MicroStation");
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, "SpeckleBootstrap.log");

      var details = new StringBuilder(message);
      if (ex is not null)
      {
        _ = details.AppendLine();
        _ = details.Append(ex.GetType().FullName);
        _ = details.Append(": ");
        _ = details.Append(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
          _ = details.AppendLine();
          _ = details.Append(ex.StackTrace);
        }
      }

      string logLine = $"[{DateTime.UtcNow:O}] [{level}] [Converter] {details}{Environment.NewLine}";
      File.AppendAllText(path, logLine);
      System.Diagnostics.Debug.WriteLine(logLine);
    }
    catch (IOException)
    {
      // never throw from converter bootstrap logging
    }
    catch (UnauthorizedAccessException)
    {
      // never throw from converter bootstrap logging
    }
    catch (ArgumentException)
    {
      // never throw from converter bootstrap logging
    }
    catch (NotSupportedException)
    {
      // never throw from converter bootstrap logging
    }
    catch (InvalidOperationException)
    {
      // never throw from converter bootstrap logging
    }
  }
}
