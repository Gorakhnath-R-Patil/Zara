namespace Zara.Indexing.Governance;

/// <summary>
/// ARCHITECTURE.md §25.1's governor — T48's scope is deliberately the
/// foreground/idle/battery rows of §25.1's table plus
/// <c>PROCESS_MODE_BACKGROUND_BEGIN</c>, not the full table. CPU%, free RAM,
/// disk queue length, and CPU temperature (WMI, best-effort) are real,
/// separate follow-up signals this v1 does not read — each needs its own
/// provider and its own decision to be worth building, and none of them
/// changes the shape of this interface when they're added later.
/// </summary>
public interface IResourceGovernor
{
    WorkBudget GetCurrentBudget();
}
