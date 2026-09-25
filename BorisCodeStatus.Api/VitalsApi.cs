using BorisCodeStatus.Core.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BorisCodeStatus.Api;

/// <summary>Host configuration for the relay endpoint.</summary>
public sealed record VitalsApiOptions
{
    /// <summary>Environment variable that overrides the port without an appsettings edit.</summary>
    public const string PortEnvironmentVariable = "BORISCODESTATUS_PORT";

    public int Port { get; init; } = 5080;

    /// <summary>
    /// Bound to 0.0.0.0 so the ESP32 can reach it across the LAN. See the security note in the README:
    /// the endpoint is unauthenticated by design in v1.
    /// </summary>
    public string BindAddress { get; init; } = "0.0.0.0";

    /// <summary>
    /// Whether to host the poller for the undocumented usage API at all. Even when hosted it only
    /// calls out if the user has opted in via <see cref="UsageApiPreference"/>; this switch exists
    /// so tests can leave the poller out entirely.
    /// </summary>
    public bool EnableUsageApiFallback { get; init; } = true;

    public static VitalsApiOptions FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
        return int.TryParse(raw, out var port) && port is > 0 and < 65536
            ? new VitalsApiOptions { Port = port }
            : new VitalsApiOptions();
    }
}

/// <summary>
/// Builds the ESP32-facing HTTP endpoint. Kept as its own project so it can be run and tested
/// standalone, but in production the tray app hosts this <see cref="WebApplication"/> in-process —
/// one process to install, run and tray-manage.
/// </summary>
public static class VitalsApi
{
    public static WebApplication Build(VitalsApiOptions? options = null, VitalsStateStore? store = null)
    {
        options ??= VitalsApiOptions.FromEnvironment();
        store ??= VitalsStateStore.Default;

        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(options);

        // Harmless for the ESP32 (not a browser) but lets a future web dashboard read the endpoint.
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        if (options.EnableUsageApiFallback)
        {
            builder.Services.AddHostedService<UsageApiRefreshService>();
        }

        var app = builder.Build();
        app.UseCors();

        // The one route that matters. Current re-reads state.json if the hook process has written
        // since the last call, so the response is always as fresh as the last hook event.
        // With the usage-API fallback off, its fields are nulled here rather than trusted to be
        // absent from the file: state.json may still hold a value fetched before it was disabled.
        app.MapGet("/status", (VitalsStateStore state) => Results.Json(
            UsageApiPreference.IsEnabled() ? state.Current : state.Current.WithoutUsageApiFields()));

        // Cheap liveness probe so the display can distinguish "relay down" from "no data yet".
        app.MapGet("/health", () => Results.Json(new { ok = true, utc = DateTimeOffset.UtcNow }));

        return app;
    }
}
