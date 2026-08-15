using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Zara.EngineClient.Process.JobObjectInterop;

namespace Zara.EngineClient.Process;

/// <summary>
/// A Windows Job Object configured with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>
/// — ARCHITECTURE.md §9.4: "spawn Zara.Engine as a child (job object:
/// kill-on-close by default)". Assigning the Engine process to this job
/// means that if the App process dies unexpectedly (crash, `kill -9`,
/// Task Manager "End Task") WITHOUT a clean shutdown, Windows itself
/// guarantees the Engine child is killed too — there is no way for the App
/// to "forget" to clean it up, because the kernel does it, not App code.
/// </summary>
public sealed class EngineJobObject : IDisposable
{
    private readonly SafeFileHandle _jobHandle;

    public EngineJobObject()
    {
        _jobHandle = CreateJobObject(0, lpName: null);
        if (_jobHandle.IsInvalid)
        {
            throw new InvalidOperationException($"CreateJobObject failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };

        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            int error = Marshal.GetLastWin32Error();
            _jobHandle.Dispose();
            throw new InvalidOperationException($"SetInformationJobObject failed (Win32 error {error}).");
        }
    }

    /// <summary>Assigns a process to this job. A process can belong to only
    /// one job at a time on older Windows versions — assigning a process
    /// that's already in a job can fail; that's surfaced as a returned
    /// <see langword="false"/>, not swallowed.</summary>
    public bool Assign(SafeProcessHandle processHandle) => AssignProcessToJobObject(_jobHandle, processHandle);

    /// <summary>Disposing the job handle — with no processes having been
    /// explicitly removed — triggers <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>:
    /// every process still assigned to this job is terminated by the kernel.</summary>
    public void Dispose() => _jobHandle.Dispose();
}
