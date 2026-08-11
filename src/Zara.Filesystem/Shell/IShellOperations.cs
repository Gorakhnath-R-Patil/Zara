namespace Zara.Filesystem.Shell;

/// <summary>
/// Wraps <c>IFileOperation</c> (via <c>Vanara.Windows.Shell.ShellFileOperations</c>
/// — see the package reference comment in Directory.Packages.props for why
/// this uses a vetted library rather than hand-rolled COM interop) —
/// ARCHITECTURE.md §10.3/§23's <c>IShellOperations</c>.
/// </summary>
public interface IShellOperations
{
    Task<OperationOutcome> ExecuteAsync(
        FileOperationBatch batch, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
