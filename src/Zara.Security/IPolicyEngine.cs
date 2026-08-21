namespace Zara.Security;

public enum PolicyOutcome { Allow, RequireConfirmation, Block }

public sealed record PolicyDecision(PolicyOutcome Outcome, RiskClass Risk, string? Reason = null);

/// <summary>
/// The gate every mutating operation passes through before execution —
/// ARCHITECTURE.md §15's policy engine. <see cref="Classify"/> is pure risk
/// classification; <see cref="Evaluate"/> is the actual gating decision.
/// Kept as two methods because a caller (e.g. a future UI) legitimately
/// wants the risk class on its own for display, independent of what the
/// gate decides to do with it.
/// </summary>
public interface IPolicyEngine
{
    RiskClass Classify(OperationContext context);

    PolicyDecision Evaluate(OperationContext context);
}

/// <inheritdoc cref="IPolicyEngine"/>
public sealed class PolicyEngine : IPolicyEngine
{
    private readonly IRiskClassifier _classifier;

    public PolicyEngine(IRiskClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public RiskClass Classify(OperationContext context) => _classifier.Classify(context);

    public PolicyDecision Evaluate(OperationContext context)
    {
        RiskClass risk = Classify(context);

        return risk switch
        {
            RiskClass.Safe or RiskClass.Low => new PolicyDecision(PolicyOutcome.Allow, risk),
            RiskClass.Blocked => new PolicyDecision(PolicyOutcome.Block, risk, "This operation is not permitted."),
            _ => new PolicyDecision(PolicyOutcome.RequireConfirmation, risk),
        };
    }
}
