using System.Runtime.InteropServices;

namespace TwinCatMcp.Core;

/// <summary>
/// OLE message filter that automatically retries COM calls rejected by a busy
/// Visual Studio / TcXaeShell instance (RPC_E_CALL_REJECTED / SERVERCALL_RETRYLATER).
/// This is the pattern recommended by Beckhoff for the Automation Interface.
/// </summary>
public sealed class MessageFilter : IOleMessageFilter
{
    /// <summary>Maximum time (ms) a rejected call is retried before giving up.</summary>
    public static int RetryTimeoutMs { get; set; } = 60_000;

    [ThreadStatic]
    private static int? _scopedRetryTimeoutMs;

    /// <summary>
    /// Limits retries of rejected calls on the current thread until disposed - for probing IDEs that may be busy
    /// (e.g. still starting) where waiting the full <see cref="RetryTimeoutMs"/> is not useful.
    /// </summary>
    public static IDisposable ShortRetries(int timeoutMs)
    {
        var previous = _scopedRetryTimeoutMs;
        _scopedRetryTimeoutMs = timeoutMs;
        return new Scope(() => _scopedRetryTimeoutMs = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    public static void Register()
    {
        CoRegisterMessageFilter(new MessageFilter(), out _);
    }

    public static void Revoke()
    {
        CoRegisterMessageFilter(null, out _);
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => 0; // SERVERCALL_ISHANDLED

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        const int SERVERCALL_RETRYLATER = 2;
        if (dwRejectType == SERVERCALL_RETRYLATER && dwTickCount < (_scopedRetryTimeoutMs ?? RetryTimeoutMs))
        {
            return 150; // retry after 150 ms
        }

        return -1; // cancel the call
    }

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => 2; // PENDINGMSG_WAITDEFPROCESS

    [DllImport("Ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
