using System.Windows.Threading;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// WPF objects need a <see cref="Dispatcher"/> on an STA thread, and MSTest doesn't provide
/// either by default — this runs a test body on a fresh STA thread, with a real
/// <see cref="DispatcherSynchronizationContext"/> installed and a <see cref="Dispatcher.PushFrame"/>
/// message loop pumped for the whole duration.
///
/// Both matter for real (not faked) async I/O: without an installed
/// <see cref="DispatcherSynchronizationContext"/>, an <c>await</c> continuation after a genuine
/// async operation resumes on an arbitrary thread-pool thread instead of the STA thread that owns
/// the UI objects — confirmed the hard way against a real device, where every <c>FakeTransport</c>
/// test passed (its "async" calls never actually suspend, so the continuation trivially stays on
/// the calling thread) but the same test against a real <c>TcpTransport</c> immediately threw
/// "The calling thread cannot access this object because a different thread owns it." Installing
/// the synchronization context fixes where continuations land; actually running them requires a
/// pumped dispatcher loop for the outer action's whole lifetime — a plain blocking
/// <c>.GetAwaiter().GetResult()</c> would deadlock (the continuation needs this same thread to be
/// pumping messages, but that call blocks it instead), so this pushes one <see cref="DispatcherFrame"/>
/// that only stops once <paramref name="asyncAction"/> completes, not per-call.
/// </summary>
internal static class StaTestRunner
{
    public static void Run(Func<Task> asyncAction)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            var frame = new DispatcherFrame();

            async Task RunAndSignal()
            {
                try
                {
                    await asyncAction();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    frame.Continue = false;
                }
            }

            _ = RunAndSignal();
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            // Rethrow with the original stack trace - a bare `throw exception;` here reported every
            // failure as coming from this line, hiding where in the test or app it really happened.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>Services one round of pending dispatcher operations — WPF's "DoEvents" equivalent. Nested `PushFrame` calls are supported by design (e.g. modal dialogs rely on this).</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <returns><see langword="true"/> if <paramref name="condition"/> became true before <paramref name="timeout"/> elapsed.</returns>
    public static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            DoEvents();
            Thread.Sleep(10);
        }

        return true;
    }
}
