namespace Zara.Filesystem.Paths;

/// <summary>
/// The known folders Zara cares about. Deliberately a small, closed set —
/// not every FOLDERID Windows defines, just the ones ARCHITECTURE.md's
/// default Tier-3 zones (§11.2) and sidebar (§21.1) reference. Extend as
/// those sections grow, not speculatively.
/// </summary>
public enum KnownFolder
{
    Profile,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Videos,
    Music,
    LocalAppData,
    RoamingAppData,
}
