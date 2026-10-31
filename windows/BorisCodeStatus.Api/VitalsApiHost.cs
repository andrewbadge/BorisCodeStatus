using BorisCodeStatus.Core.State;
using Microsoft.AspNetCore.Builder;

namespace BorisCodeStatus.Api;

/// <summary>
/// Owns the listener's lifetime so it can be stopped and started again while the tray keeps
/// running. Pausing releases the TCP port outright rather than answering with an error status:
/// the point is that the display sees a refused connection, which its existing "relay down"
/// path already handles, instead of a 503 it would have to learn about.
///
/// A stopped <see cref="WebApplication"/> cannot be restarted, so resuming builds a fresh one.
/// That is why this holds the options and store rather than the app itself.
///
/// Start and Stop are safe to call from a UI thread. Both bridge async work synchronously, and
/// doing that directly on a thread with a <see cref="SynchronizationContext"/> deadlocks: the
/// framework posts its continuations back to that thread, which is blocked waiting for them.
/// A cancellation token does not save you — cancelling does not release a continuation that can
/// never be scheduled. <see cref="RunDetached"/> is what actually prevents it.
/// </summary>
public sealed class VitalsApiHost : IDisposable
{
    private readonly VitalsApiOptions _options;
    private readonly VitalsStateStore _store;
    private readonly Lock _gate = new();

    private WebApplication? _app;
    private bool _disposed;

    public VitalsApiHost(VitalsApiOptions options, VitalsStateStore store)
    {
        _options = options;
        _store = store;
    }

    /// <summary>Whether the port is currently bound.</summary>
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _app is not null;
            }
        }
    }

    /// <summary>
    /// Binds the port. Throws if it cannot — the caller decides whether that is fatal, because
    /// at startup it is worth a dialog and on a resume it is worth a balloon.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_app is not null)
            {
                return;
            }

            var app = VitalsApi.Build(_options, _store);

            // StartAsync rather than RunAsync: the caller's thread must return to pump the
            // WinForms message loop.
            RunDetached(() => app.StartAsync());
            _app = app;
        }
    }

    /// <summary>
    /// Releases the port. Also stops the background usage-API polling, which is a hosted service
    /// inside the same app — intended, since a paused relay should be doing nothing at all.
    /// </summary>
    public void Stop()
    {
        WebApplication? app;
        lock (_gate)
        {
            app = _app;
            _app = null;
        }

        if (app is null)
        {
            return;
        }

        try
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            RunDetached(() => app.StopAsync(shutdown.Token));
            RunDetached(() => app.DisposeAsync().AsTask());
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting the listener down anyway; a hung stop must not take the tray with it.
        }
    }

    /// <summary>
    /// Runs async work on the thread pool and waits for it, so the ambient
    /// <see cref="SynchronizationContext"/> is never captured. Without this, calling from a UI
    /// thread hangs forever: the continuations are posted back to the very thread that is blocked
    /// here waiting for them. The wait itself is short — this is a bind or an unbind, not I/O.
    /// </summary>
    private static void RunDetached(Func<Task> work) =>
        Task.Run(work).GetAwaiter().GetResult();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
