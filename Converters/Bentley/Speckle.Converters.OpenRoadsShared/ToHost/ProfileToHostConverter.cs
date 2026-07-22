using Microsoft.Extensions.Logging;
using Speckle.Objects.Data;
using Speckle.Sdk;

namespace Speckle.Converters.OpenRoads.ToHost;

/// <summary>
/// Rebuilds a vertical profile on an alignment from a Speckle profile DataObject.
/// </summary>
/// <remarks>
/// Best-effort: the profile's display geometry (station/elevation space) is rebuilt as a
/// <see cref="CifLG.LinearComplex"/> and attached via <c>Alignment.CreateProfileByProfileElement(...)</c>
/// (the ATRL/Atom pattern used a <c>ProfileComplex</c>). Faithfully reconstructing vertical curves as CifNET
/// profile elements (PVIs, parabolas) is the follow-up; profile creation is a live-SDK surface and is
/// flagged. A profile that can't be rebuilt is skipped - the alignment still stands.
/// </remarks>
public class ProfileToHostConverter(
  CivilLinearElementBuilder linearElementBuilder,
  ILogger<ProfileToHostConverter> logger
)
{
  public void Create(DataObject profileObject, CifGM.Alignment alignment)
  {
    var complex = linearElementBuilder.Build(profileObject);
    if (complex is null)
    {
      return;
    }

    try
    {
      // (element, addToActiveProfile, makeActive) - matches the ATRL/Atom CreateProfileByProfileElement usage
      alignment.CreateProfileByProfileElement(complex, true, true);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogDebug(ex, "Could not rebuild profile '{Name}' on alignment", profileObject.name);
    }
  }
}
