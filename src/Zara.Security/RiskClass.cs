namespace Zara.Security;

/// <summary>
/// ARCHITECTURE.md §17.2's risk table. Ordered low-to-high — code that needs
/// "is A at least as risky as B" can compare the enum values directly, and
/// escalation logic (§17.2: "Risk escalates, never de-escalates: an
/// operation's class is max() over all its items and all applicable rules")
/// is a literal <c>Math.Max</c> over these.
/// </summary>
public enum RiskClass
{
    Safe = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
    Blocked = 5,
}
