using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Receive;

/// <summary>
/// Bakes a received model version into the active DGN model.
/// NOTE (MVP): objects are baked onto the active level; per-collection level creation, material/color
/// proxies and instance definitions are follow-ups. Re-receiving does not yet delete previously baked
/// elements.
/// </summary>
public class MicroStationHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly IReceiveConversionHandler _conversionHandler;
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _converterSettings;

  public MicroStationHostObjectBuilder(
    IRootToHostConverter converter,
    RootObjectUnpacker rootObjectUnpacker,
    IReceiveConversionHandler conversionHandler,
    IConverterSettingsStore<MicroStationConversionSettings> converterSettings
  )
  {
    _converter = converter;
    _rootObjectUnpacker = rootObjectUnpacker;
    _conversionHandler = conversionHandler;
    _converterSettings = converterSettings;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    onOperationProgressed.Report(new("Converting", null));

    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);
    var (atomicObjects, _) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(unpackedRoot.ObjectsToConvert);

    var results = new HashSet<ReceiveConversionResult>();
    var bakedObjectIds = new HashSet<string>();
    int count = 0;

    foreach (var traversalContext in atomicObjects)
    {
      Base atomicObject = traversalContext.Current;
      onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));
      var ex = _conversionHandler.TryConvert(() =>
      {
        cancellationToken.ThrowIfCancellationRequested();

        var convertedElements = ConvertAndBake(atomicObject);

        foreach (var element in convertedElements)
        {
          string elementId = element.ElementId.ToString();
          bakedObjectIds.Add(elementId);
          results.Add(new(Status.SUCCESS, atomicObject, elementId, element.GetType().ToString()));
        }
      });
      if (ex is not null)
      {
        results.Add(new(Status.ERROR, atomicObject, null, null, ex));
      }
    }

    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, results));
  }

  private List<BDE.Element> ConvertAndBake(Base atomicObject)
  {
    var baked = new List<BDE.Element>();
    object converted = _converter.Convert(atomicObject);

    switch (converted)
    {
      case BDE.Element element:
        AddToModel(element, baked);
        break;

      // data object conversions return element/base pairs
      case IEnumerable<(BDE.Element, Base)> typedList:
        foreach (var (element, _) in typedList)
        {
          AddToModel(element, baked);
        }
        break;

      // display value fallback conversions are zipped as (object, Base) pairs by ConverterWithFallback
      case IEnumerable<(object, Base)> fallbackList:
        foreach (var (obj, _) in fallbackList)
        {
          if (obj is BDE.Element element)
          {
            AddToModel(element, baked);
          }
        }
        break;

      case IEnumerable<BDE.Element> elements:
        foreach (var element in elements)
        {
          AddToModel(element, baked);
        }
        break;

      default:
        throw new SpeckleConversionException(
          $"Conversion of {atomicObject.speckle_type} returned an unexpected result: {converted?.GetType().Name}"
        );
    }

    return baked;
  }

  private static void AddToModel(BDE.Element element, List<BDE.Element> baked)
  {
    var status = element.AddToModel();
    if (status == BDPN.StatusInt.Error)
    {
      throw new SpeckleConversionException("Failed to add element to the model.");
    }

    baked.Add(element);
  }
}

/// <summary>
/// Exception thrown when baking a converted object into the DGN model fails.
/// </summary>
public class SpeckleConversionException : Exception
{
  public SpeckleConversionException() { }

  public SpeckleConversionException(string message)
    : base(message) { }

  public SpeckleConversionException(string message, Exception innerException)
    : base(message, innerException) { }
}
