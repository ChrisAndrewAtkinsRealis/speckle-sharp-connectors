using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.MicroStation.Operations.Send.Settings;

/// <summary>
/// Per-model-card toggle controlling whether elements from attached references (read-only attached DGN/DWG
/// files) are included when publishing. Off by default: references are external, read-only data, so pulling
/// them in is opt-in.
/// </summary>
public class IncludeReferencesSetting(bool value = IncludeReferencesSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "includeReferences";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Include Reference Data";
  public string? Description { get; set; } =
    "Include elements from attached references (read-only attached models). Off by default.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
