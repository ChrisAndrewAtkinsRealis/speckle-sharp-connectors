using Speckle.Sdk.Models;

namespace Speckle.Connectors.MicroStation.Operations.Receive;

/// <summary>
/// Rebuilds native civil (OpenRoads/OpenRail) entities from received Speckle objects, so the receive host
/// builder stays vertical-agnostic. No-op for plain MicroStation.
/// </summary>
public interface ICivilHostRebuilder
{
  /// <summary>True if this object is a civil entity this rebuilder handles (e.g. an Alignment or Corridor DataObject).</summary>
  bool CanRebuild(Base obj);

  /// <summary>Rebuilds the native civil entity and returns the baked native element ids.</summary>
  IReadOnlyList<string> Rebuild(Base obj);
}

/// <summary>No-op rebuilder for the plain MicroStation connector (no civil model).</summary>
public sealed class NullCivilHostRebuilder : ICivilHostRebuilder
{
  public bool CanRebuild(Base obj) => false;

  public IReadOnlyList<string> Rebuild(Base obj) => [];
}
