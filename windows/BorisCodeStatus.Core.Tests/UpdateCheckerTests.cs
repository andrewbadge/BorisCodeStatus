using System.Net;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The opt-in update check: off by default, and reading GitHub's answer without ever throwing.</summary>
public class UpdateCheckerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public UpdateCheckerTests() => Directory.CreateDirectory(_directory);

    /// <summary>The default has to hold with nothing on disk: an install nobody configured makes no network calls.</summary>
    [Fact]
    public void OffByDefault()
    {
        Assert.False(UpdateCheckPreference.IsEnabled(SettingsPath));
        Assert.Null(UpdateCheckPreference.NotifiedVersion(SettingsPath));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void RemembersBeingEnabledAndTheVersionAnnounced()
    {
        Assert.True(UpdateCheckPreference.TrySetEnabled(true, SettingsPath));
        Assert.True(UpdateCheckPreference.TrySetNotifiedVersion("1.8.0", SettingsPath));

        Assert.True(UpdateCheckPreference.IsEnabled(SettingsPath));
        Assert.Equal("1.8.0", UpdateCheckPreference.NotifiedVersion(SettingsPath));
    }

    [Theory]
    [InlineData("v1.8.0", "1.8.0")]
    [InlineData("1.8.0", "1.8.0")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData(" v1.8 ", "1.8")]
    public void ParsesReleaseTags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.8.0-beta.1")]
    [InlineData("v1.2.3.4")]
    public void IgnoresTagsItCannotUse(string? tag) => Assert.Null(UpdateChecker.ParseTag(tag));

    [Theory]
    [InlineData("1.8.0", "1.7.0", true)]
    [InlineData("1.7.1", "1.7.0", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.7.0", "1.7.0", false)]
    [InlineData("1.6.0", "1.7.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.7", "1.7.0", false)]
    [InlineData("1.8.0", "unknown", false)]
    public void ComparesNumericallyNotAsText(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(Version.Parse(latest), current));

    [Fact]
    public async Task ReadsTheTagFromGitHubsAnswerAndSendsOnlyAUserAgent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "tag_name": "v1.9.0", "name": "x" }""");

        var latest = await UpdateChecker.GetLatestAsync(new HttpClient(handler));

        Assert.Equal(new Version(1, 9, 0), latest);
        Assert.Equal(UpdateChecker.LatestReleaseUrl, handler.Request!.RequestUri!.ToString());
        Assert.Equal("BorisCodeStatus", handler.Request.Headers.UserAgent.ToString());
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.Forbidden, "rate limited")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, """{ "tag_name": 5 }""")]
    public async Task AnyFailureIsNoAnswerRatherThanAnException(HttpStatusCode status, string body) =>
        Assert.Null(await UpdateChecker.GetLatestAsync(new HttpClient(new StubHandler(status, body))));

    [Fact]
    public async Task ANetworkErrorIsNoAnswer() =>
        Assert.Null(await UpdateChecker.GetLatestAsync(new HttpClient(new StubHandler(throws: true))));

    private sealed class StubHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "", bool throws = false) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return throws
                ? throw new HttpRequestException("offline")
                : Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp directory; leaving it behind is harmless.
        }
    }
}
