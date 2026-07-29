using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.MicroStation.ToHost.Helpers;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Sdk;

namespace Speckle.Converters.MicroStation;

public static class ServiceRegistration
{
  public static void AddMicroStationConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    // register interface-generated types (settings factory etc.)
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    // add single root converter + top level to speckle/to host converters
    serviceCollection.AddRootCommon<MicroStationRootToSpeckleConverter>(converterAssembly);

    // add application converters (raw typed converters) and unit converter
    serviceCollection.AddApplicationConverters<MicroStationToSpeckleUnitConverter, string>(converterAssembly);

    // conversion settings store
    serviceCollection.AddScoped<
      IConverterSettingsStore<MicroStationConversionSettings>,
      ConverterSettingsStore<MicroStationConversionSettings>
    >();

    // one reference origin per send/receive operation, shared by every raw geometry converter (see
    // ReferencePointConverter) so civil/GIS-scale absolute coordinates don't lose precision downstream
    serviceCollection.AddScoped<IReferencePointConverter, ReferencePointConverter>();

    // helpers
    serviceCollection.AddScoped<PropertiesExtractor>();
    serviceCollection.AddScoped<ItemTypePropertiesExtractor>();
    serviceCollection.AddScoped<ToSpeckle.TopLevel.ElementToSpeckleDataObjectBuilder>();
    serviceCollection.AddScoped<MicroStationUnitScaler>();
  }
}
