using System.Net;
using System.Net.Sockets;
using BorisClaudeNotifications.Api;
using BorisClaudeNotifications.Core.State;

namespace BorisClaudeNotifications.Core.Tests;

/// <summary>
/// Covers pausing and resuming the listener, and the deadlock that pausing from the tray caused
/// before <c>RunDetached</c> existed.
/// </summary>
public class VitalsApiHostTests : IDisposable
{
    private readonly string _stateFile = Path.Combine(Path.GetTempPath(), $"fido-host-{Guid.NewGuid():N}.json");

    /// <summary>Asks the OS for a free port, so parallel runs cannot collide on a fixed one.</summary>
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private VitalsApiHost NewHost(int port) => new(
        new VitalsApiOptions { Port = port, BindAddress = "127.0.0.1", EnableUsageApiFallback = false },
        new VitalsStateStore(_stateFile));

    private static bool CanConnect(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException)
        {
            return false;
        }
    }

    [Fact]
    public void PausingClosesThePortAndResumingReopensIt()
    {
        var port = FreePort();
        using var host = NewHost(port);

        host.Start();
        Assert.True(host.IsListening);
        Assert.True(CanConnect(port));

        host.Stop();
        Assert.False(host.IsListening);
        Assert.False(CanConnect(port));

        // A stopped WebApplication cannot be restarted, so this only works because the host
        // builds a fresh one. Regression guard for anyone "simplifying" it to hold the app.
        host.Start();
        Assert.True(host.IsListening);
        Assert.True(CanConnect(port));
    }

    [Fact]
    public void StartAndStopAreIdempotent()
    {
        var port = FreePort();
        using var host = NewHost(port);

        host.Stop();  // never started
        host.Start();
        host.Start(); // already listening
        Assert.True(host.IsListening);

        host.Stop();
        host.Stop();
        Assert.False(host.IsListening);
    }

    /// <summary>
    /// The tray hung when pausing because Start/Stop bridged async work synchronously on a thread
    /// carrying a SynchronizationContext: the framework posted continuations back to the very
    /// thread blocked waiting for them. This reproduces that with a context that queues posts and
    /// never runs them — exactly a UI thread stuck inside a click handler.
    /// </summary>
    [Fact]
    public void DoesNotDeadlockOnAThreadWithASynchronizationContext()
    {
        var port = FreePort();
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());
            try
            {
                using var host = NewHost(port);
                host.Start();
                host.Stop();
                host.Start();
                host.Stop();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        worker.IsBackground = true;
        worker.Start();

        Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "Start/Stop deadlocked under a SynchronizationContext.");
        Assert.Null(failure);
    }

    /// <summary>Accepts posted callbacks and never runs them, like a blocked UI message pump.</summary>
    private sealed class NeverPumpedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Deliberately dropped: anything that awaits back onto this context never resumes.
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            File.Delete(_stateFile);
        }
        catch (IOException)
        {
            // Temp file; leaving it is harmless.
        }
    }
}
