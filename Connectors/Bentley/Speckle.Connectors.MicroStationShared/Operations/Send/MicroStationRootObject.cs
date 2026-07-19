namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// A record pairing an element with its stable speckle application id.
/// </summary>
public record MicroStationRootObject(BDE.Element Root, string ApplicationId);
