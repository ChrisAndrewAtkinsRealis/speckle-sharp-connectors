using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Contributes civil (OpenRoads/OpenRail) entities to the send root. Implemented as a no-op for plain
/// MicroStation and as a real contributor for the civil verticals, so the root object builder stays
/// vertical-agnostic.
/// </summary>
public interface ICivilModelContributor
{
  /// <summary>
  /// Enumerates civil entities from the active geometric model(s), converts them, and adds them to a
  /// <c>Civil</c> collection under <paramref name="root"/>. Returns the per-object conversion results.
  /// </summary>
  IReadOnlyList<SendConversionResult> Contribute(Collection root, CancellationToken cancellationToken);
}

/// <summary>No-op contributor used by the plain MicroStation connector (no civil model).</summary>
public sealed class NullCivilModelContributor : ICivilModelContributor
{
  public IReadOnlyList<SendConversionResult> Contribute(Collection root, CancellationToken cancellationToken) => [];
}
