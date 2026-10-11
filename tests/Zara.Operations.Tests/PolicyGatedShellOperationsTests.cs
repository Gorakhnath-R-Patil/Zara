using System.Diagnostics;
using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Filesystem.Shell;
using Zara.Security;

namespace Zara.Operations.Tests;

/// <summary>
/// The policy gate in front of <see cref="IShellOperations"/>. The executor is
/// a fake throughout, so these tests are fast and can assert the one thing
/// that matters most: when the gate says no, the real executor is never called.
/// Two cases use the real <see cref="PolicyEngine"/> to prove the wiring
/// reaches the actual blocked-root and protected-executable rules.
/// </summary>
public sealed class PolicyGatedShellOperationsTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "zara-gate-test-" + Guid.NewGuid().ToString("N"));

    public PolicyGatedShellOperationsTests() => Directory.CreateDirectory(_tempRoot);

    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        // Removing the link itself (non-recursive) leaves its target alone;
        // a recursive delete refuses to cross a junction.
        foreach (var junction in _junctions)
        {
            try { Directory.Delete(junction, recursive: false); } catch (IOException) { /* best effort */ }
        }

        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { /* best effort */ }
    }

    // ── fakes ──────────────────────────────────────────────────────────

    private sealed class RecordingShell : IShellOperations
    {
        public int Calls { get; private set; }

        public Task<OperationOutcome> ExecuteAsync(
            FileOperationBatch batch, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new OperationOutcome(true,
                batch.Items.Select(i => new FileOperationItemOutcome(i.Source, true, i.Source, null)).ToList()));
        }
    }

    private sealed class FixedPolicy(PolicyDecision decision) : IPolicyEngine
    {
        public int Evaluations { get; private set; }
        public OperationContext? LastContext { get; private set; }

        public RiskClass Classify(OperationContext context) => decision.Risk;

        public PolicyDecision Evaluate(OperationContext context)
        {
            Evaluations++;
            LastContext = context;
            return decision;
        }
    }

    private sealed class ScriptedConfirmer(bool answer) : IOperationConfirmer
    {
        public OperationConfirmationRequest? Request { get; private set; }

        public Task<bool> ConfirmAsync(OperationConfirmationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(answer);
        }
    }

    private static CanonicalPath Canonical(string path) => CanonicalPath.FromCanonicalizedString(@"\\?\" + path);

    private FileOperationBatch DeleteBatch(params string[] paths) =>
        new(FileOperationKind.Delete, paths.Select(p => new FileOperationItem(Canonical(p))).ToList());

    private static PolicyDecision Decision(PolicyOutcome outcome, RiskClass risk, string? reason = null) =>
        new(outcome, risk, reason);

    // ── gate behaviour ─────────────────────────────────────────────────

    [Fact]
    public async Task Allow_RunsTheInnerExecutor()
    {
        var shell = new RecordingShell();
        var sut = new PolicyGatedShellOperations(shell, new FixedPolicy(Decision(PolicyOutcome.Allow, RiskClass.Low)), new DenyingOperationConfirmer());

        var outcome = await sut.ExecuteAsync(DeleteBatch(Path.Combine(_tempRoot, "a.txt")));

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, shell.Calls);
    }

    [Fact]
    public async Task Block_NeverCallsTheInnerExecutor_AndFailsEveryItemWithTheReason()
    {
        var shell = new RecordingShell();
        var policy = new FixedPolicy(Decision(PolicyOutcome.Block, RiskClass.Blocked, "nope"));
        var sut = new PolicyGatedShellOperations(shell, policy, new ScriptedConfirmer(answer: true));

        var outcome = await sut.ExecuteAsync(DeleteBatch(Path.Combine(_tempRoot, "a.txt"), Path.Combine(_tempRoot, "b.txt")));

        Assert.Equal(0, shell.Calls);
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Items.Count);
        Assert.All(outcome.Items, i =>
        {
            Assert.False(i.Succeeded);
            Assert.Contains("nope", i.ErrorMessage);
        });
    }

    [Fact]
    public async Task Block_IsNotOverridableByAConfirmerThatSaysYes()
    {
        var shell = new RecordingShell();
        var confirmer = new ScriptedConfirmer(answer: true);
        var sut = new PolicyGatedShellOperations(shell, new FixedPolicy(Decision(PolicyOutcome.Block, RiskClass.Blocked)), confirmer);

        await sut.ExecuteAsync(DeleteBatch(Path.Combine(_tempRoot, "a.txt")));

        Assert.Equal(0, shell.Calls);
        Assert.Null(confirmer.Request);
    }

    [Fact]
    public async Task RequireConfirmation_Denied_DoesNotRun()
    {
        var shell = new RecordingShell();
        var sut = new PolicyGatedShellOperations(shell, new FixedPolicy(Decision(PolicyOutcome.RequireConfirmation, RiskClass.High)), new DenyingOperationConfirmer());

        var outcome = await sut.ExecuteAsync(DeleteBatch(Path.Combine(_tempRoot, "a.txt")));

        Assert.Equal(0, shell.Calls);
        Assert.False(outcome.Succeeded);
        Assert.Contains("High", outcome.Items.Single().ErrorMessage);
    }

    [Fact]
    public async Task RequireConfirmation_Approved_RunsAndShowsTheConfirmerTheBatch()
    {
        var shell = new RecordingShell();
        var confirmer = new ScriptedConfirmer(answer: true);
        var decision = Decision(PolicyOutcome.RequireConfirmation, RiskClass.Medium);
        var sut = new PolicyGatedShellOperations(shell, new FixedPolicy(decision), confirmer);
        var batch = DeleteBatch(Path.Combine(_tempRoot, "a.txt"));

        var outcome = await sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, shell.Calls);
        Assert.NotNull(confirmer.Request);
        Assert.Same(batch, confirmer.Request!.Batch);
        Assert.Equal(decision, confirmer.Request.Decision);
    }

    [Fact]
    public async Task EmptyBatch_PassesThrough_WithoutConsultingThePolicy()
    {
        var shell = new RecordingShell();
        var policy = new FixedPolicy(Decision(PolicyOutcome.Block, RiskClass.Blocked));
        var sut = new PolicyGatedShellOperations(shell, policy, new DenyingOperationConfirmer());

        await sut.ExecuteAsync(new FileOperationBatch(FileOperationKind.Delete, []));

        Assert.Equal(0, policy.Evaluations);
        Assert.Equal(1, shell.Calls);
    }

    // ── the facts handed to the classifier ─────────────────────────────

    private async Task<OperationContext> ContextFor(FileOperationBatch batch)
    {
        var policy = new FixedPolicy(Decision(PolicyOutcome.Allow, RiskClass.Safe));
        await new PolicyGatedShellOperations(new RecordingShell(), policy, new DenyingOperationConfirmer()).ExecuteAsync(batch);
        return policy.LastContext!;
    }

    [Fact]
    public async Task Context_SumsFileAndFolderBytes()
    {
        string file = Path.Combine(_tempRoot, "one.bin");
        File.WriteAllBytes(file, new byte[1000]);
        string folder = Path.Combine(_tempRoot, "dir");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[200]);
        File.WriteAllBytes(Path.Combine(folder, "nested", "b.bin"), new byte[30]);

        var context = await ContextFor(DeleteBatch(file, folder));

        Assert.Equal(1230, context.TotalBytes);
        Assert.Equal(2, context.ItemCount);
    }

    [Fact]
    public async Task Context_MissingItem_CountsAsZeroBytes_InsteadOfFailingTheGate()
    {
        var context = await ContextFor(DeleteBatch(Path.Combine(_tempRoot, "does-not-exist.txt")));

        Assert.Equal(0, context.TotalBytes);
    }

    [Fact]
    public async Task Context_ResolvesMoveDestination_FromFolderAndName()
    {
        string source = Path.Combine(_tempRoot, "photo.jpg");
        string destFolder = Path.Combine(_tempRoot, "archive");
        var move = new FileOperationBatch(FileOperationKind.Move, [new FileOperationItem(Canonical(source), Canonical(destFolder))]);
        var renamedMove = new FileOperationBatch(FileOperationKind.Move, [new FileOperationItem(Canonical(source), Canonical(destFolder), "old.jpg")]);

        Assert.Equal(@"\\?\" + Path.Combine(destFolder, "photo.jpg"), (await ContextFor(move)).Plan.Items.Single().DestPath!.Value.Value);
        Assert.Equal(@"\\?\" + Path.Combine(destFolder, "old.jpg"), (await ContextFor(renamedMove)).Plan.Items.Single().DestPath!.Value.Value);
    }

    [Fact]
    public async Task Context_ResolvesRenameDestination_InTheSameFolder()
    {
        string source = Path.Combine(_tempRoot, "draft.txt");
        var rename = new FileOperationBatch(FileOperationKind.Rename, [new FileOperationItem(Canonical(source), NewName: "final.txt")]);

        var dest = (await ContextFor(rename)).Plan.Items.Single().DestPath!.Value;

        Assert.Equal(@"\\?\" + Path.Combine(_tempRoot, "final.txt"), dest.Value);
    }

    [Fact]
    public async Task Context_Delete_HasNoDestination_AndIsNeverPermanent()
    {
        var context = await ContextFor(DeleteBatch(Path.Combine(_tempRoot, "x.txt")));

        Assert.Null(context.Plan.Items.Single().DestPath);
        Assert.Equal(OperationKind.Delete, context.Plan.Kind);
        Assert.False(context.IsPermanentDelete);
    }

    [Fact]
    public async Task Context_FlagsCrossVolumeCopies()
    {
        var copy = new FileOperationBatch(FileOperationKind.Copy,
            [new FileOperationItem(CanonicalPath.FromCanonicalizedString(@"\\?\C:\a\file.txt"), CanonicalPath.FromCanonicalizedString(@"\\?\D:\b"))]);
        var sameVolume = new FileOperationBatch(FileOperationKind.Copy,
            [new FileOperationItem(CanonicalPath.FromCanonicalizedString(@"\\?\C:\a\file.txt"), CanonicalPath.FromCanonicalizedString(@"\\?\c:\b"))]);

        Assert.True((await ContextFor(copy)).CrossesVolumes);
        Assert.False((await ContextFor(sameVolume)).CrossesVolumes);
    }

    [Fact]
    public async Task Context_FlagsAJunctionAsAReparsePoint()
    {
        string target = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(target);
        string junction = Path.Combine(_tempRoot, "link");
        Assert.True(TryCreateJunction(junction, target), "mklink /J unavailable in this environment — cannot exercise this case.");
        _junctions.Add(junction);

        var viaJunction = await ContextFor(DeleteBatch(junction));
        var viaPlainFolder = await ContextFor(DeleteBatch(target));

        Assert.True(viaJunction.TouchesReparsePoint);
        Assert.False(viaPlainFolder.TouchesReparsePoint);
    }

    // ── end to end with the real policy engine ─────────────────────────

    private static PolicyEngine RealPolicy() => new(new RiskClassifier(new BlockedRoots()));

    [Fact]
    public async Task RealPolicy_BlocksDeletingUnderTheWindowsDirectory()
    {
        var shell = new RecordingShell();
        var sut = new PolicyGatedShellOperations(shell, RealPolicy(), new ScriptedConfirmer(answer: true));
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var outcome = await sut.ExecuteAsync(DeleteBatch(Path.Combine(windows, "System32", "drivers", "etc", "hosts")));

        Assert.Equal(0, shell.Calls);
        Assert.False(outcome.Succeeded);
        Assert.Contains("Blocked", outcome.Items.Single().ErrorMessage);
    }

    [Fact]
    public async Task RealPolicy_BlocksMovingAFileOntoAnExecutableName()
    {
        var shell = new RecordingShell();
        var sut = new PolicyGatedShellOperations(shell, RealPolicy(), new ScriptedConfirmer(answer: true));
        string source = Path.Combine(_tempRoot, "notes.txt");
        var rename = new FileOperationBatch(FileOperationKind.Rename, [new FileOperationItem(Canonical(source), NewName: "notes.exe")]);

        var outcome = await sut.ExecuteAsync(rename);

        Assert.Equal(0, shell.Calls);
        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task RealPolicy_AnOrdinaryMoveInsideTheProfile_NeedsConfirmation()
    {
        var shell = new RecordingShell();
        var denied = new PolicyGatedShellOperations(shell, RealPolicy(), new DenyingOperationConfirmer());
        var approved = new PolicyGatedShellOperations(shell, RealPolicy(), new ScriptedConfirmer(answer: true));
        string source = Path.Combine(_tempRoot, "a.txt");
        var move = new FileOperationBatch(FileOperationKind.Move,
            [new FileOperationItem(Canonical(source), Canonical(Path.Combine(_tempRoot, "dest")))]);

        Assert.False((await denied.ExecuteAsync(move)).Succeeded);
        Assert.Equal(0, shell.Calls);

        Assert.True((await approved.ExecuteAsync(move)).Succeeded);
        Assert.Equal(1, shell.Calls);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
