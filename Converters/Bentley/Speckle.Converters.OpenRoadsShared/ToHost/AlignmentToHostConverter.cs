using Bentley.CifNET.SDK.Edit;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Data;
using Speckle.Sdk;

namespace Speckle.Converters.OpenRoads.ToHost;

/// <summary>
/// Rebuilds a native OpenRoads/OpenRail horizontal alignment from a Speckle alignment DataObject.
/// </summary>
/// <remarks>
/// Mirrors the proven ATRL/Atom ORD flow: build a <see cref="CifLG.LinearComplex"/> from the baseline curves,
/// then <c>GeometricModel.CreateAlignmentByLinearElement(complex, true)</c>, set the feature definition, and
/// add stationing - all inside a transient session that is committed with <c>PersistTransients()</c>. The
/// CifNET create/stationing calls are a live-SDK surface and are flagged.
/// </remarks>
public class AlignmentToHostConverter(
  CivilLinearElementBuilder linearElementBuilder,
  ILogger<AlignmentToHostConverter> logger
)
{
  public const string DEFAULT_FEATURE_DEFINITION = "Alignment\\Geom_Baseline";

  /// <summary>
  /// Creates the alignment within the caller's transient session (the caller is responsible for
  /// <c>StartTransientMode</c>/<c>PersistTransients</c>). Returns null when the baseline can't be rebuilt.
  /// </summary>
  public CifGM.Alignment? Create(DataObject alignmentObject, CifGM.GeometricModel geometricModel)
  {
    var complex = linearElementBuilder.Build(alignmentObject);
    if (complex is null)
    {
      logger.LogWarning("Alignment '{Name}' had no rebuildable baseline geometry", alignmentObject.name);
      return null;
    }

    var alignment = geometricModel.CreateAlignmentByLinearElement(complex, true);

    string name = string.IsNullOrEmpty(alignmentObject.name) ? "Alignment" : alignmentObject.name!;
    string featureDefinition =
      alignmentObject.properties.TryGetValue("featureDefinition", out var fd) && fd is string fds && fds.Length > 0
        ? fds
        : DEFAULT_FEATURE_DEFINITION;

    try
    {
      alignment.SetFeatureDefinition(featureDefinition, name);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Could not set feature definition on alignment {Name}", name);
    }

    // stationing (start station) - AlignmentEdit exposes AddStationing in transient mode
    if (
      TryGetDouble(alignmentObject.properties, "startStation", out double startStation)
      && alignment is Bentley.CifNET.GeometryModel.SDK.Edit.AlignmentEdit alignmentEdit
    )
    {
      try
      {
        alignmentEdit.AddStationing(0, startStation, true);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogDebug(ex, "Could not add stationing to alignment {Name}", name);
      }
    }

    return alignment;
  }

  internal static bool TryGetDouble(IReadOnlyDictionary<string, object?> properties, string key, out double value)
  {
    value = 0;
    if (!properties.TryGetValue(key, out var raw) || raw is null)
    {
      return false;
    }

    try
    {
      value = System.Convert.ToDouble(raw);
      return true;
    }
    catch (Exception)
    {
      return false;
    }
  }
}
