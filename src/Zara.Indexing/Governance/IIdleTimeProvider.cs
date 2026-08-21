namespace Zara.Indexing.Governance;

public interface IIdleTimeProvider
{
    /// <summary>Time since the last keyboard/mouse input, system-wide (not
    /// just within Zara) — via <c>GetLastInputInfo</c>.</summary>
    TimeSpan GetIdleTime();
}
