using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Resolves elements that live in attached references (read-only attached models).
/// </summary>
/// <remarks>
/// In MicroStation, unlike AutoCAD Xrefs, every element in a reference is individually selectable and
/// snappable as if it were in the active model — it is simply read-only until the reference is activated.
/// Each selected reference element carries its own <see cref="Bentley.DgnPlatformNET.DgnModelRef"/>, which for
/// a reference is a <see cref="Bentley.DgnPlatformNET.DgnAttachment"/>. We therefore give reference elements a
/// composite application id of <c>R{attachmentElementId}:{elementId}</c> so they can be told apart from
/// active-model elements (plain numeric ids) and resolved back through the attachment.
///
/// NOTE: attachment enumeration/resolution is a Bentley-API-dependent surface; only top-level attachments are
/// handled (nested references are a follow-up). Reference geometry is converted in the active model's units —
/// references authored with different master units or a scaled/rotated attachment transform need per-reference
/// settings / transform application, which is also a follow-up.
/// </remarks>
public class MicroStationReferenceService
{
  private const string REFERENCE_PREFIX = "R";
  private readonly ILogger<MicroStationReferenceService> _logger;

  public MicroStationReferenceService(ILogger<MicroStationReferenceService> logger)
  {
    _logger = logger;
  }

  public static bool IsReferenceId(string applicationId) => applicationId.StartsWith(REFERENCE_PREFIX, StringComparison.Ordinal);

  /// <summary>
  /// Builds the composite id for an element selected inside a reference attachment.
  /// </summary>
  public static string EncodeReferenceId(BDPN.DgnAttachment attachment, BDE.Element element) =>
    $"{REFERENCE_PREFIX}{attachment.ElementId}:{element.ElementId}";

  private static bool TryDecode(string applicationId, out ulong attachmentId, out ulong elementId)
  {
    attachmentId = 0;
    elementId = 0;

    if (!IsReferenceId(applicationId))
    {
      return false;
    }

    var parts = applicationId.Substring(REFERENCE_PREFIX.Length).Split(':');
    return parts.Length == 2
      && ulong.TryParse(parts[0], out attachmentId)
      && ulong.TryParse(parts[1], out elementId);
  }

  /// <summary>
  /// Resolves a reference element and the attachment it belongs to (needed to highlight/snap in the view).
  /// </summary>
  public (BDE.Element? element, BDPN.DgnModelRef? modelRef) Resolve(BDPN.DgnModel activeModel, string applicationId)
  {
    if (!TryDecode(applicationId, out ulong attachmentId, out ulong elementId))
    {
      return (null, null);
    }

    try
    {
      var attachment = FindAttachment(activeModel, attachmentId);
      var referenceModel = attachment?.GetDgnModel();
      var element = referenceModel?.FindElementById((BDPN.ElementId)elementId);
      return (element, attachment);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to resolve reference element {ApplicationId}", applicationId);
      return (null, null);
    }
  }

  private BDPN.DgnAttachment? FindAttachment(BDPN.DgnModel activeModel, ulong attachmentId)
  {
    foreach (var attachment in EnumerateAttachments(activeModel))
    {
      if (attachment.ElementId == (BDPN.ElementId)attachmentId)
      {
        return attachment;
      }
    }

    return null;
  }

  private IEnumerable<BDPN.DgnAttachment> EnumerateAttachments(BDPN.DgnModel activeModel)
  {
    try
    {
      // OfType tolerates whatever concrete collection GetDgnAttachments returns (and skips nulls)
      return activeModel.GetDgnAttachments()?.OfType<BDPN.DgnAttachment>() ?? Enumerable.Empty<BDPN.DgnAttachment>();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to enumerate reference attachments");
      return Enumerable.Empty<BDPN.DgnAttachment>();
    }
  }
}
