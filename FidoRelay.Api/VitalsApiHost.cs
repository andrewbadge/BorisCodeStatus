using FidoRelay.Core.State;
using Microsoft.AspNetCore.Builder;

namespace FidoRelay.Api;

/// <summary>
/// Owns the listener's lifetime so it can be stopped and started again while the tray keeps
/// running. Pausing releases the TCP port outright rather than answering with an error status:
/// the point is that the display sees a refused connection, which its existing "relay down"
/// path already handles, instead of a 503 it would have to learn about.
///
/// A stopped <see cref="WebApplication"/> cannot be restarted, so resuming builds a fresh one.
/// That is why this holds the options and store rather than the app itself.
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
            app.StartAsync().GetAwaiter().GetResult();
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
            app.StopAsync(shutdown.Token).GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting the listener down anyway; a hung stop must not take the tray with it.
        }
    }

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
