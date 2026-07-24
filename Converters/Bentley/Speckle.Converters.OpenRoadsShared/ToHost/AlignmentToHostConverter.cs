using System.Reflection;
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
  public object? Create(DataObject alignmentObject, object geometricModel)
  {
    IDisposable? complexDisposable = null;
    try
    {
      complexDisposable = linearElementBuilder.Build(alignmentObject);
      if (complexDisposable is not CifLG.LinearComplex complex)
      {
        logger.LogWarning("Alignment '{Name}' had no rebuildable baseline geometry", alignmentObject.name);
        return null;
      }

      object? alignment = geometricModel
        .GetType()
        .GetMethod("CreateAlignmentByLinearElement", BindingFlags.Instance | BindingFlags.Public)
        ?.Invoke(geometricModel, [complex, true]);
      if (alignment is null)
      {
        logger.LogWarning("Could not create alignment '{Name}' from geometric model", alignmentObject.name);
        return null;
      }

      string name = string.IsNullOrEmpty(alignmentObject.name) ? "Alignment" : alignmentObject.name!;
      string featureDefinition =
        alignmentObject.properties.TryGetValue("featureDefinition", out var fd) && fd is string fds && fds.Length > 0
          ? fds
          : DEFAULT_FEATURE_DEFINITION;

      try
      {
        alignment.GetType().GetMethod("SetFeatureDefinition", BindingFlags.Instance | BindingFlags.Public)?.Invoke(alignment, [featureDefinition, name]);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogDebug(ex, "Could not set feature definition on alignment {Name}", name);
      }

      // stationing (start station) - use late binding because the Bentley 2026 edit types differ by SDK layout
      if (TryGetDouble(alignmentObject.properties, "startStation", out double startStation))
      {
        try
        {
          alignment.GetType().GetMethod("AddStationing", BindingFlags.Instance | BindingFlags.Public)?.Invoke(alignment, [0d, startStation, true]);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          logger.LogDebug(ex, "Could not add stationing to alignment {Name}", name);
        }
      }

      return alignment;
    }
    finally
    {
      complexDisposable?.Dispose();
    }
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
      value = Convert.ToDouble(raw);
      return true;
    }
    catch (FormatException)
    {
      return false;
    }
    catch (InvalidCastException)
    {
      return false;
    }
    catch (OverflowException)
    {
      return false;
    }
  }
}
