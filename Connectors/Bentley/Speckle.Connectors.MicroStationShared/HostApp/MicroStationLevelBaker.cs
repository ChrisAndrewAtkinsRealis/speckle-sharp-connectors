using Bentley.DgnPlatformNET;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Creates DGN levels (layers) and assigns received elements to them on receive.
/// </summary>
/// <remarks>
/// Adapted from proven ATRL/Atom DGN writer patterns. Two Bentley specifics captured here:
/// <list type="bullet">
/// <item><c>Element.LevelId</c> is getter-only — the write path is
/// <c>ElementPropertiesSetter.SetLevel(id).Apply(element)</c>, not a property set.</item>
/// <item>New levels only become durable after <c>FileLevelCache.Write()</c>; without it elements
/// silently fall back to the default level (0). So we create the whole batch up front and write once.</item>
/// </list>
/// </remarks>
public class MicroStationLevelBaker
{
  private readonly IConverterSettingsStore<MicroStationConversionSettings> _settingsStore;
  private readonly ILogger<MicroStationLevelBaker> _logger;

  public MicroStationLevelBaker(
    IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
    ILogger<MicroStationLevelBaker> logger
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
  }

  /// <summary>
  /// Creates every missing level up front, then flushes the cache once (cheap when nothing is new).
  /// Call before baking elements so level ids are already durable when elements reference them.
  /// </summary>
  public void EnsureLevels(IEnumerable<string> levelNames)
  {
    try
    {
      FileLevelCache levelCache = _settingsStore.Current.File.GetLevelCache();
      bool anyCreated = false;

      foreach (string raw in levelNames)
      {
        string name = string.IsNullOrWhiteSpace(raw) ? "Default" : raw.Trim();
        if (!levelCache.GetLevelByName(name).IsValid)
        {
          levelCache.CreateLevel(name);
          anyCreated = true;
        }
      }

      if (anyCreated)
      {
        levelCache.Write();
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to pre-create DGN levels");
    }
  }

  /// <summary>
  /// Assigns an element to a level by name, creating the level if needed.
  /// </summary>
  public void SetElementLevel(BDE.Element element, string levelName)
  {
    try
    {
      LevelId levelId = GetOrCreateLevel(levelName);
      using var setter = new ElementPropertiesSetter();
      setter.SetLevel(levelId);
      setter.Apply(element);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to set level {LevelName} on element", levelName);
    }
  }

  private LevelId GetOrCreateLevel(string levelName)
  {
    if (string.IsNullOrWhiteSpace(levelName))
    {
      levelName = "Default";
    }

    FileLevelCache levelCache = _settingsStore.Current.File.GetLevelCache();

    LevelHandle handle = levelCache.GetLevelByName(levelName);
    if (handle.IsValid)
    {
      return handle.LevelId;
    }

    // dynamically-added level missed by EnsureLevels: create and write immediately
    EditLevelHandle newHandle = levelCache.CreateLevel(levelName);
    if (newHandle.IsValid)
    {
      levelCache.Write();
      return newHandle.LevelId;
    }

    LevelHandle fallback = levelCache.GetLevelByName("Default");
    return fallback.IsValid ? fallback.LevelId : default;
  }
}
