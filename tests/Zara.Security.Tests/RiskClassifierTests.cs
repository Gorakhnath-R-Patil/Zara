using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Security;

namespace Zara.Security.Tests;

public class RiskClassifierTests
{
    private const string ProfileRoot = @"C:\Users\testuser";
    private static readonly string[] BlockedTestRoots = [@"C:\Windows", @"C:\Program Files", @"C:\ZaraTestData"];

    private readonly RiskClassifier _sut = new(new FixedBlockedRoots(BlockedTestRoots), ProfileRoot);

    private static CanonicalPath P(string path) => CanonicalPath.FromCanonicalizedString(@"\\?\" + path);

    private static OperationContext Ctx(
        OperationKind kind,
        IReadOnlyList<OperationItemPlan> items,
        long totalBytes = 0,
        bool crossesVolumes = false,
        bool touchesReparsePoint = false,
        bool isPermanentDelete = false) =>
        new(new OperationPlan(kind, items), totalBytes, crossesVolumes, touchesReparsePoint, isPermanentDelete);

    private static OperationItemPlan Item(string source, string? dest = null) =>
        new(P(source), dest is null ? null : P(dest));

    // ── Baselines ────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_Create_InProfile_IsLow()
    {
        var ctx = Ctx(OperationKind.Create, [Item($@"{ProfileRoot}\NewFolder")]);

        Assert.Equal(RiskClass.Low, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Rename_SmallInProfile_IsMedium()
    {
        var ctx = Ctx(OperationKind.Rename, [Item($@"{ProfileRoot}\a.txt", $@"{ProfileRoot}\b.txt")]);

        Assert.Equal(RiskClass.Medium, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Move_SmallInProfile_IsMedium()
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\a.txt", $@"{ProfileRoot}\sub\a.txt")]);

        Assert.Equal(RiskClass.Medium, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Copy_SmallInProfile_IsMedium()
    {
        var ctx = Ctx(OperationKind.Copy, [Item($@"{ProfileRoot}\a.txt", $@"{ProfileRoot}\a-copy.txt")]);

        Assert.Equal(RiskClass.Medium, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Delete_SmallInProfile_IsHigh()
    {
        var ctx = Ctx(OperationKind.Delete, [Item($@"{ProfileRoot}\a.txt")]);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    // ── Escalation rules ─────────────────────────────────────────────────────

    [Fact]
    public void Classify_MoveOver50Items_EscalatesToHigh()
    {
        var items = Enumerable.Range(0, 51).Select(i => Item($@"{ProfileRoot}\f{i}.txt", $@"{ProfileRoot}\dest\f{i}.txt")).ToList();
        var ctx = Ctx(OperationKind.Move, items);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Exactly50Items_StaysMedium()
    {
        var items = Enumerable.Range(0, 50).Select(i => Item($@"{ProfileRoot}\f{i}.txt", $@"{ProfileRoot}\dest\f{i}.txt")).ToList();
        var ctx = Ctx(OperationKind.Move, items);

        Assert.Equal(RiskClass.Medium, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_CrossesVolumes_EscalatesToHigh()
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\a.txt", @"D:\backup\a.txt")], crossesVolumes: true);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_OutsideUserProfile_EscalatesToHigh()
    {
        var ctx = Ctx(OperationKind.Move, [Item(@"C:\SomeOtherPlace\a.txt", @"C:\SomeOtherPlace\b.txt")]);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_TouchesReparsePoint_EscalatesToCritical()
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\link", $@"{ProfileRoot}\dest\link")], touchesReparsePoint: true);

        Assert.Equal(RiskClass.Critical, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_DeleteOver100Items_EscalatesToCritical()
    {
        var items = Enumerable.Range(0, 101).Select(i => Item($@"{ProfileRoot}\f{i}.txt")).ToList();
        var ctx = Ctx(OperationKind.Delete, items);

        Assert.Equal(RiskClass.Critical, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_DeleteOver1Gb_EscalatesToCritical()
    {
        var ctx = Ctx(OperationKind.Delete, [Item($@"{ProfileRoot}\huge.bin")], totalBytes: 2L * 1024 * 1024 * 1024);

        Assert.Equal(RiskClass.Critical, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_DeleteExactly1Gb_StaysHigh_ThresholdIsExclusive()
    {
        var ctx = Ctx(OperationKind.Delete, [Item($@"{ProfileRoot}\a.bin")], totalBytes: 1L * 1024 * 1024 * 1024);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_MoveOver100ItemsButNotDelete_DoesNotReachCritical()
    {
        // The >100-items Critical escalation is delete-specific (§17.2) — a
        // large move stops at High via the >50-items rule, not Critical.
        var items = Enumerable.Range(0, 200).Select(i => Item($@"{ProfileRoot}\f{i}.txt", $@"{ProfileRoot}\dest\f{i}.txt")).ToList();
        var ctx = Ctx(OperationKind.Move, items);

        Assert.Equal(RiskClass.High, _sut.Classify(ctx));
    }

    // ── Blocked ──────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_SourceUnderBlockedRoot_IsBlocked()
    {
        var ctx = Ctx(OperationKind.Delete, [Item(@"C:\Windows\System32\notepad.exe")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_DestUnderBlockedRoot_IsBlocked()
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\a.txt", @"C:\Program Files\a.txt")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_SiblingOfBlockedRoot_IsNotBlocked_SegmentComparisonNotStartsWith()
    {
        // "C:\Windows-ish" must NOT match a blocked root of "C:\Windows" —
        // the same ordinal-segment-comparison discipline as PathValidator (§17.3 item 6).
        var ctx = Ctx(OperationKind.Delete, [Item(@"C:\Windows-ish\file.txt")]);

        Assert.NotEqual(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_PermanentDelete_IsAlwaysBlocked()
    {
        var ctx = Ctx(OperationKind.Delete, [Item($@"{ProfileRoot}\a.txt")], isPermanentDelete: true);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_WritingToAnExecutableDestination_IsBlocked()
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\a.txt", $@"{ProfileRoot}\payload.exe")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Theory]
    [InlineData(".dll")]
    [InlineData(".sys")]
    [InlineData(".msi")]
    public void Classify_WritingToOtherProtectedExtensions_IsBlocked(string extension)
    {
        var ctx = Ctx(OperationKind.Move, [Item($@"{ProfileRoot}\a.txt", $@"{ProfileRoot}\thing{extension}")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_VolumeRootAsSource_IsBlocked()
    {
        var ctx = Ctx(OperationKind.Delete, [Item(@"C:")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_ZaraOwnDataDirectory_IsBlocked()
    {
        var ctx = Ctx(OperationKind.Delete, [Item(@"C:\ZaraTestData\audit\audit-202601.jsonl")]);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    [Fact]
    public void Classify_Blocked_TakesPrecedenceOverEveryOtherEscalation()
    {
        // Even a HUGE, cross-volume, reparse-point-touching operation is
        // still just "Blocked", not "Blocked but also somehow Critical" —
        // Blocked is terminal.
        var ctx = Ctx(
            OperationKind.Delete,
            [Item(@"C:\Windows\System32\notepad.exe")],
            totalBytes: 10L * 1024 * 1024 * 1024,
            crossesVolumes: true,
            touchesReparsePoint: true);

        Assert.Equal(RiskClass.Blocked, _sut.Classify(ctx));
    }

    private sealed class FixedBlockedRoots(IEnumerable<string> roots) : IBlockedRoots
    {
        public IReadOnlyList<CanonicalPath> Roots { get; } = roots.Select(P).ToList();
    }
}
