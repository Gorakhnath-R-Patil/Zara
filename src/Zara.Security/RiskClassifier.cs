using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Filesystem.Paths;

namespace Zara.Security;

public interface IRiskClassifier
{
    RiskClass Classify(OperationContext context);
}

/// <inheritdoc cref="IRiskClassifier"/>
/// <remarks>
/// Implements ARCHITECTURE.md §17.2's table literally, including "risk
/// escalates, never de-escalates: an operation's class is <c>max()</c> over
/// all its items and all applicable rules" — every rule below can only push
/// the result up, never down, and the final value is the max across all of
/// them. Pure and deterministic: no filesystem access, no I/O — everything
/// it needs is already in <see cref="OperationContext"/>, which is exactly
/// what makes T49's 10,000-iteration fuzz test possible without a real
/// filesystem behind it.
/// </remarks>
public sealed class RiskClassifier : IRiskClassifier
{
    private const int MediumMaxItems = 50;
    private const int CriticalItemThreshold = 100;
    private const long CriticalByteThreshold = 1L * 1024 * 1024 * 1024; // 1 GB

    private static readonly string[] ProtectedExecutableExtensions = [".exe", ".dll", ".sys", ".msi"];

    private readonly IBlockedRoots _blockedRoots;
    private readonly CanonicalPath _userProfileRoot;

    public RiskClassifier(IBlockedRoots blockedRoots, string? userProfileRoot = null)
    {
        _blockedRoots = blockedRoots ?? throw new ArgumentNullException(nameof(blockedRoots));

        string profile = userProfileRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _userProfileRoot = CanonicalPath.FromCanonicalizedString(@"\\?\" + Path.GetFullPath(profile).TrimEnd('\\'));
    }

    public RiskClass Classify(OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsBlocked(context))
        {
            return RiskClass.Blocked;
        }

        // Baseline: LOW for create (§17.2: "create_folder... -> auto,
        // logged"), HIGH for delete-to-Recycle-Bin, MEDIUM for everything
        // else (rename/move/copy within the small/in-profile common case).
        RiskClass risk = context.Plan.Kind switch
        {
            OperationKind.Create => RiskClass.Low,
            OperationKind.Delete => RiskClass.High,
            _ => RiskClass.Medium,
        };

        if (context.ItemCount > MediumMaxItems)
        {
            risk = Max(risk, RiskClass.High);
        }

        if (context.CrossesVolumes)
        {
            risk = Max(risk, RiskClass.High);
        }

        if (!IsWithinUserProfile(context))
        {
            risk = Max(risk, RiskClass.High);
        }

        if (context.TouchesReparsePoint)
        {
            risk = Max(risk, RiskClass.Critical);
        }

        if (context.Plan.Kind == OperationKind.Delete &&
            (context.ItemCount > CriticalItemThreshold || context.TotalBytes > CriticalByteThreshold))
        {
            risk = Max(risk, RiskClass.Critical);
        }

        return risk;
    }

    private bool IsBlocked(OperationContext context)
    {
        if (context.IsPermanentDelete)
        {
            return true;
        }

        foreach (var item in context.Plan.Items)
        {
            if (IsUnderAnyBlockedRoot(item.SourcePath))
            {
                return true;
            }

            if (item.DestPath is { } dest)
            {
                if (IsUnderAnyBlockedRoot(dest) || IsProtectedExecutable(dest))
                {
                    return true;
                }
            }

            if (IsVolumeRoot(item.SourcePath))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsUnderAnyBlockedRoot(CanonicalPath path)
    {
        foreach (var root in _blockedRoots.Roots)
        {
            if (PathSyntax.IsSameOrDescendant(path.Value, root.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProtectedExecutable(CanonicalPath path)
    {
        string extension = Path.GetExtension(path.Value);
        return ProtectedExecutableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True for exactly <c>\\?\C:</c> / <c>\\?\C:\</c> — a volume
    /// root itself as an operation target (e.g. "delete C:\") — never a
    /// descendant of one, which is the ordinary, unblocked case.</summary>
    private static bool IsVolumeRoot(CanonicalPath path)
    {
        if (!path.Value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return false;
        }

        string rest = path.Value[4..].TrimEnd('\\');
        return rest.Length == 2 && rest[1] == ':';
    }

    private bool IsWithinUserProfile(OperationContext context)
    {
        foreach (var item in context.Plan.Items)
        {
            if (!PathSyntax.IsSameOrDescendant(item.SourcePath.Value, _userProfileRoot.Value))
            {
                return false;
            }

            if (item.DestPath is { } dest && !PathSyntax.IsSameOrDescendant(dest.Value, _userProfileRoot.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static RiskClass Max(RiskClass a, RiskClass b) => (RiskClass)Math.Max((int)a, (int)b);
}
