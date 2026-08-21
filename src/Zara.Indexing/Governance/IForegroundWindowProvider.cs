namespace Zara.Indexing.Governance;

public interface IForegroundWindowProvider
{
    /// <summary>True if the current process owns the foreground window —
    /// distinguishes "Zara foreground, user active" from "other app
    /// foreground, user active" in §25.1's budget table.</summary>
    bool IsCurrentProcessForeground();
}
