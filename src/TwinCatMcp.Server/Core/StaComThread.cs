using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace TwinCatMcp.Core;

/// <summary>
/// Dedicated single-threaded-apartment thread that owns every COM object of the
/// TwinCAT Automation Interface / Visual Studio DTE. All COM calls MUST go through
/// <see cref="InvokeAsync{T}"/> so they are serialized on the same apartment.
/// The thread pumps Windows messages while idle, which is required for COM callbacks
/// and for the OLE message filter (retry on "application is busy").
/// </summary>
public sealed class StaComThread : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _disposed;

    public StaComThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "TwinCAT-COM-STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    public bool IsCurrentThread => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;

    private void Run()
    {
        MessageFilter.Register();
        try
        {
            while (!_disposed)
            {
                while (_queue.TryTake(out var work))
                {
                    work();
                }

                // Wait for new work while pumping messages (COM needs a message loop in STA).
                Win32.MsgWaitForMultipleObjectsEx(1, new[] { _signal.SafeWaitHandle.DangerousGetHandle() },
                    200, Win32.QS_ALLINPUT, Win32.MWMO_INPUTAVAILABLE);
                Win32.PumpPendingMessages();
            }
        }
        finally
        {
            MessageFilter.Revoke();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> func, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsCurrentThread)
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (tcs.Task.IsCompleted)
            {
                return;
            }

            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        _signal.Set();

        if (timeout is null && !cancellationToken.CanBeCanceled)
        {
            return tcs.Task;
        }

        return tcs.Task.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public Task InvokeAsync(Action action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => InvokeAsync<object?>(() => { action(); return null; }, timeout, cancellationToken);

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
        _queue.CompleteAdding();
    }

    private static class Win32
    {
        public const uint QS_ALLINPUT = 0x04FF;
        public const uint MWMO_INPUTAVAILABLE = 0x0004;
        private const uint PM_REMOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr[] pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        public static void PumpPendingMessages()
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
    }
}
