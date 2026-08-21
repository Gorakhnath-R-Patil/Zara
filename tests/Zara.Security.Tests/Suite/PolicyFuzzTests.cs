using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Security;

namespace Zara.Security.Tests.Suite;

/// <summary>
/// T49 / ARCHITECTURE.md §27.1's POLICY suite:
/// <code>
/// Fuzz 10,000 random OperationRequests through IPolicyEngine
///   -> no request under BLOCKED_ROOTS ever returns Allow
///   -> risk class is monotonic in item count and byte count
/// </code>
/// "Model output fuzzing: 10,000 malformed/hostile JSON objects -> none
/// produce an executed operation" is NOT covered here — that needs the
/// agent/tool-execution layer (Phase 3), which doesn't exist yet. This
/// covers the half that's real today: the deterministic policy gate itself.
/// </summary>
public class PolicyFuzzTests
{
    private const string ProfileRoot = @"C:\Users\fuzzuser";
    private static readonly string[] BlockedTestRoots = [@"C:\Windows", @"C:\Program Files", @"C:\ZaraTestData"];
    private static readonly string[] SafeSegments = ["Documents", "Downloads", "Projects", "notes", "sub1", "sub2"];
    private static readonly string[] Extensions = ["txt", "pdf", "docx", "jpg", "exe", "dll"];
    private static readonly OperationKind[] Kinds = [OperationKind.Move, OperationKind.Copy, OperationKind.Rename, OperationKind.Delete, OperationKind.Create];

    private readonly PolicyEngine _sut = new(new RiskClassifier(new FixedBlockedRoots(BlockedTestRoots), ProfileRoot));

    [Fact]
    public void Fuzz_10000RandomOperations_NoBlockedRootRequestEverReturnsAllow()
    {
        var random = new Random(20260809);

        for (int i = 0; i < 10_000; i++)
        {
            var context = GenerateRandomOperation(random, out bool touchesBlockedRoot);
            var decision = _sut.Evaluate(context);

            if (touchesBlockedRoot)
            {
                Assert.NotEqual(PolicyOutcome.Allow, decision.Outcome);
                Assert.Equal(RiskClass.Blocked, decision.Risk);
            }
        }
    }

    [Fact]
    public void Fuzz_10000RandomOperations_RiskIsMonotonicInItemCount()
    {
        var random = new Random(20260810);

        for (int i = 0; i < 10_000; i++)
        {
            var (smaller, larger) = GenerateItemCountPair(random);

            var smallerRisk = _sut.Classify(smaller);
            var largerRisk = _sut.Classify(larger);

            // Adding MORE items to an otherwise-identical operation must
            // never make it look SAFER — this is §17.2's "risk escalates,
            // never de-escalates" as a directly testable property.
            Assert.True(largerRisk >= smallerRisk,
                $"Adding items reduced risk: {smallerRisk} (fewer items) -> {largerRisk} (more items).");
        }
    }

    [Fact]
    public void Fuzz_10000RandomOperations_RiskIsMonotonicInByteCount()
    {
        var random = new Random(20260811);

        for (int i = 0; i < 10_000; i++)
        {
            var context = GenerateBaseDeleteOperation(random, out long baseBytes);
            var withMoreBytes = context with { TotalBytes = baseBytes + random.Next(1, int.MaxValue) };

            var baseRisk = _sut.Classify(context);
            var moreBytesRisk = _sut.Classify(withMoreBytes);

            Assert.True(moreBytesRisk >= baseRisk,
                $"Increasing total bytes reduced risk: {baseRisk} -> {moreBytesRisk}.");
        }
    }

    private OperationContext GenerateRandomOperation(Random random, out bool touchesBlockedRoot)
    {
        var kind = Kinds[random.Next(Kinds.Length)];
        int itemCount = random.Next(1, 150);
        bool useBlockedRoot = random.Next(10) == 0; // 10% of the time, root the path under a blocked root
        touchesBlockedRoot = useBlockedRoot;

        var items = new List<OperationItemPlan>(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            string sourceRoot = useBlockedRoot ? BlockedTestRoots[random.Next(BlockedTestRoots.Length)] : ProfileRoot;
            string source = $@"{sourceRoot}\{SafeSegments[random.Next(SafeSegments.Length)]}\file{i}.{Extensions[random.Next(Extensions.Length)]}";
            string? dest = kind is OperationKind.Move or OperationKind.Copy or OperationKind.Rename
                ? $@"{ProfileRoot}\dest\file{i}.{Extensions[random.Next(Extensions.Length)]}"
                : null;

            items.Add(new OperationItemPlan(P(source), dest is null ? null : P(dest)));
        }

        return new OperationContext(
            new OperationPlan(kind, items),
            TotalBytes: random.Next(0, 2_000_000_000),
            CrossesVolumes: random.Next(20) == 0,
            TouchesReparsePoint: random.Next(20) == 0,
            IsPermanentDelete: false);
    }

    private (OperationContext Smaller, OperationContext Larger) GenerateItemCountPair(Random random)
    {
        var kind = Kinds[random.Next(Kinds.Length)];
        int baseCount = random.Next(1, 60);
        int extraCount = random.Next(1, 100);

        var baseItems = MakeItems(kind, baseCount);
        var largerItems = new List<OperationItemPlan>(baseItems);
        largerItems.AddRange(MakeItems(kind, extraCount, startIndex: baseCount));

        long bytes = random.Next(0, 500_000_000);
        bool crossesVolumes = random.Next(20) == 0;
        bool touchesReparsePoint = random.Next(20) == 0;

        var smaller = new OperationContext(new OperationPlan(kind, baseItems), bytes, crossesVolumes, touchesReparsePoint, false);
        var larger = new OperationContext(new OperationPlan(kind, largerItems), bytes, crossesVolumes, touchesReparsePoint, false);
        return (smaller, larger);
    }

    private OperationContext GenerateBaseDeleteOperation(Random random, out long baseBytes)
    {
        int itemCount = random.Next(1, 60);
        baseBytes = random.Next(0, 500_000_000);

        return new OperationContext(
            new OperationPlan(OperationKind.Delete, MakeItems(OperationKind.Delete, itemCount)),
            baseBytes,
            CrossesVolumes: false,
            TouchesReparsePoint: false,
            IsPermanentDelete: false);
    }

    private List<OperationItemPlan> MakeItems(OperationKind kind, int count, int startIndex = 0)
    {
        var items = new List<OperationItemPlan>(count);
        for (int i = 0; i < count; i++)
        {
            int index = startIndex + i;
            string source = $@"{ProfileRoot}\Documents\file{index}.txt";
            string? dest = kind is OperationKind.Move or OperationKind.Copy or OperationKind.Rename
                ? $@"{ProfileRoot}\dest\file{index}.txt"
                : null;
            items.Add(new OperationItemPlan(P(source), dest is null ? null : P(dest)));
        }

        return items;
    }

    private static CanonicalPath P(string path) => CanonicalPath.FromCanonicalizedString(@"\\?\" + path);

    private sealed class FixedBlockedRoots(IEnumerable<string> roots) : IBlockedRoots
    {
        public IReadOnlyList<CanonicalPath> Roots { get; } = roots.Select(P).ToList();
    }
}
