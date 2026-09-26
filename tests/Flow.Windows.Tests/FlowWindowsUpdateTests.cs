using System.Net;
using System.Text;
using Flow.Updates;
using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsUpdateTests
{
    [Fact]
    public async Task ConstructionIsOfflineAndExplicitCheckUsesOneReadOnlyOfficialRequest()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Release())));
        using var transport = new HttpClient(handler);
        using var client = new FlowUpdateClient("0.2.0-alpha.4", transport, new Detector());
        Assert.Equal(0, handler.Calls);
        var result = await client.CheckAsync(UpdateChannel.Prerelease, "pt-BR");
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(FlowUpdateContract.ReleasesEndpoint, handler.Uri);
        Assert.False(handler.HasBody);
        Assert.Equal(FlowUpdateStatus.Available, result.Status);
        Assert.Equal("0.2.0-alpha.5", result.LatestVersion);
        Assert.Equal(FlowInstallationMethod.Msi, result.InstallationMethod);
    }

    [Fact]
    public async Task UserCancellationRemainsCancellation()
    {
        using var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response(Release());
        });
        using var transport = new HttpClient(handler);
        using var client = new FlowUpdateClient("0.2.0-alpha.4", transport, new Detector());
        using var cancellation = new CancellationTokenSource();
        var pending = client.CheckAsync(UpdateChannel.Prerelease, "en-US", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not-json", UpdateCheckFailureKind.InvalidResponse)]
    [InlineData("[]", UpdateCheckFailureKind.NoRelease)]
    public async Task InvalidAndEmptyResponsesHaveLocalizedRecoverableStates(string json, UpdateCheckFailureKind kind)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(json)));
        using var transport = new HttpClient(handler);
        using var client = new FlowUpdateClient("0.2.0-alpha.4", transport, new Detector());
        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => client.CheckAsync(UpdateChannel.Prerelease, "en-US"));
        Assert.Equal(kind, error.Kind);
        foreach (var language in new[] { "en-US", "pt-BR" })
        {
            Assert.NotEmpty(new FlowWindowsTextCatalog(language)[$"Updates{kind}"]);
        }
    }

    [Theory]
    [InlineData("https://github.com:444/MardSilva/FlowEngineNet/releases/tag/v0.2.0-alpha.5")]
    [InlineData("https://someone@github.com/MardSilva/FlowEngineNet/releases/tag/v0.2.0-alpha.5")]
    public async Task NonstandardReleaseOriginsAreRejected(string url)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Release(url))));
        using var transport = new HttpClient(handler);
        using var client = new FlowUpdateClient("0.2.0-alpha.4", transport, new Detector());
        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => client.CheckAsync(UpdateChannel.Prerelease, "en-US"));
        Assert.Equal(UpdateCheckFailureKind.InvalidResponse, error.Kind);
    }

    [Theory]
    [InlineData("C:/Flow/app", "C:/Flow", true, FlowInstallationMethod.Msi)]
    [InlineData("C:/Flow/app", "C:/OtherFlow", true, FlowInstallationMethod.Unknown)]
    [InlineData("C:/Flow/app", null, true, FlowInstallationMethod.Unknown)]
    [InlineData("C:/Flow/app", "C:/Flow", false, FlowInstallationMethod.Unknown)]
    [InlineData("C:/Flow/app-backup", "C:/Flow", true, FlowInstallationMethod.Unknown)]
    public void GraphicalMsiDetectionRequiresMatchingRegisteredLocation(string directory, string? registration,
        bool graphical, FlowInstallationMethod expected)
    {
        Assert.Equal(expected, DefaultInstallationMethodDetector.Detect(
            new InstallationDetectionContext(true, directory, directory, registration, false, graphical)));
    }

    private static string Release(string url = "https://github.com/MardSilva/FlowEngineNet/releases/tag/v0.2.0-alpha.5") =>
        $$"""[{"tag_name":"v0.2.0-alpha.5","draft":false,"prerelease":true,"html_url":"{{url}}","assets":[]}]""";

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class Detector : IInstallationMethodDetector
    {
        public FlowInstallationMethod Detect() => FlowInstallationMethod.Msi;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public bool HasBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri;
            HasBody = request.Content is not null;
            return respond(request, cancellationToken);
        }
    }
}
