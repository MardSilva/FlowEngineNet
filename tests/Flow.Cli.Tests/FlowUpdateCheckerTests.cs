using System.Net;
using System.Text;
using System.Text.Json;
using Flow.Cli;
using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Cli.Tests;

public sealed class FlowUpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_SelectsStableReleaseAndPublishedMsiDigest()
    {
        var source = new FakeSource(Releases(
            Release("v0.2.0-alpha.4", prerelease: true),
            Release(
                "v0.3.0",
                prerelease: false,
                "FlowEngineNet.Setup.0.3.0.pt-BR.win-x64.msi",
                new string('A', 64)),
            Release("v0.2.1", prerelease: false)));
        var checker = new FlowUpdateChecker(
            source,
            new FakeDetector(FlowInstallationMethod.Msi),
            "0.2.0-alpha.3");

        var result = await checker.CheckAsync(UpdateChannel.Stable, "pt-BR");

        Assert.Equal(FlowUpdateStatus.Available, result.Status);
        Assert.Equal("0.3.0", result.LatestVersion);
        Assert.Equal("FlowEngineNet.Setup.0.3.0.pt-BR.win-x64.msi", result.ArtifactName);
        Assert.Equal(new string('a', 64), result.ArtifactSha256);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task CheckAsync_PrereleaseChannelUsesSemanticOrdering()
    {
        var checker = CreateChecker(
            "0.2.0-alpha.2",
            FlowInstallationMethod.DotNetTool,
            Release("v0.2.0-alpha.10", prerelease: true, "FlowEngineNet.Tool.0.2.0-alpha.10.nupkg"),
            Release("v0.2.0-alpha.9", prerelease: true),
            Release("v0.1.9", prerelease: false));

        var result = await checker.CheckAsync(UpdateChannel.Prerelease, "en-US");

        Assert.Equal("0.2.0-alpha.10", result.LatestVersion);
        Assert.Equal("FlowEngineNet.Tool.0.2.0-alpha.10.nupkg", result.ArtifactName);
    }

    [Fact]
    public async Task CheckAsync_StableChannelIgnoresPrereleaseEntries()
    {
        var checker = CreateChecker(
            "0.3.0",
            FlowInstallationMethod.Unknown,
            Release("v1.0.0-beta.1", prerelease: true),
            Release("v0.3.0", prerelease: false));

        var result = await checker.CheckAsync(UpdateChannel.Stable, "en-US");

        Assert.Equal(FlowUpdateStatus.Current, result.Status);
        Assert.Equal("0.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_ReportsWhenSelectedChannelHasNoRelease()
    {
        var checker = CreateChecker(
            "0.2.0-alpha.3",
            FlowInstallationMethod.Unknown,
            Release("v0.2.0-alpha.4", prerelease: true));

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => checker.CheckAsync(UpdateChannel.Stable, "en-US"));

        Assert.Equal(UpdateCheckFailureKind.NoRelease, exception.Kind);
    }

    [Fact]
    public async Task CheckAsync_AllowsMissingPublishedChecksum()
    {
        var checker = CreateChecker(
            "0.2.0",
            FlowInstallationMethod.Portable,
            Release("v0.3.0", prerelease: false, "FlowEngineNet.0.3.0.win-x64.zip"));

        var result = await checker.CheckAsync(UpdateChannel.Stable, "en-US");

        Assert.Equal("FlowEngineNet.0.3.0.win-x64.zip", result.ArtifactName);
        Assert.Null(result.ArtifactSha256);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[{\"tag_name\":\"invalid\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/MardSilva/FlowEngineNet/releases/tag/invalid\",\"assets\":[]}]")]
    [InlineData("[{\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.com/release\",\"assets\":[]}]")]
    public async Task CheckAsync_RejectsMalformedOrUntrustedResponses(string json)
    {
        var checker = new FlowUpdateChecker(
            new FakeSource(Encoding.UTF8.GetBytes(json)),
            new FakeDetector(FlowInstallationMethod.Unknown),
            "0.2.0");

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => checker.CheckAsync(UpdateChannel.Stable, "en-US"));

        Assert.Equal(UpdateCheckFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task HttpSource_UsesOnlyOfficialEndpointAndIdentifiableUserAgent()
    {
        var handler = new RecordingHandler(Releases(Release("v0.2.0", prerelease: false)));
        using var client = new HttpClient(handler);
        using var source = new HttpUpdateReleaseSource(client);

        await source.GetReleasesAsync(CancellationToken.None);

        Assert.Equal(FlowUpdateContract.ReleasesEndpoint, handler.RequestUri);
        Assert.Contains(FlowUpdateContract.UserAgentProduct, handler.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpSource_MapsRepresentedTlsFailureToNetworkFailure()
    {
        using var client = new HttpClient(new ThrowingHandler(new HttpRequestException("TLS failed")));
        using var source = new HttpUpdateReleaseSource(client);

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => source.GetReleasesAsync(CancellationToken.None));

        Assert.Equal(UpdateCheckFailureKind.Network, exception.Kind);
    }

    [Fact]
    public async Task HttpSource_MapsOfflineFailureToNetworkFailure()
    {
        using var client = new HttpClient(new ThrowingHandler(new HttpRequestException("offline")));
        using var source = new HttpUpdateReleaseSource(client);

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => source.GetReleasesAsync(CancellationToken.None));

        Assert.Equal(UpdateCheckFailureKind.Network, exception.Kind);
    }

    [Fact]
    public async Task HttpSource_MapsInternalCancellationToTimeout()
    {
        using var client = new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout")));
        using var source = new HttpUpdateReleaseSource(client);

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => source.GetReleasesAsync(CancellationToken.None));

        Assert.Equal(UpdateCheckFailureKind.Timeout, exception.Kind);
    }

    [Fact]
    public async Task HttpSource_RejectsRedirectWithoutFollowingIt()
    {
        var handler = new RedirectingHandler();
        using var client = new HttpClient(handler);
        using var source = new HttpUpdateReleaseSource(client);

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => source.GetReleasesAsync(CancellationToken.None));

        Assert.Equal(UpdateCheckFailureKind.InvalidResponse, exception.Kind);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(true, "C:\\Users\\Flow\\App", "C:\\other", "C:\\Users\\Flow\\App\\", true, FlowInstallationMethod.Msi)]
    [InlineData(false, "/home/user/.dotnet/tools", "/home/user/.dotnet/tools/.store/flowenginenet.tool/0.2.0/", null, false, FlowInstallationMethod.DotNetTool)]
    [InlineData(true, "C:\\Flow", "C:\\Flow", null, true, FlowInstallationMethod.Portable)]
    [InlineData(false, "/opt/flow", "/opt/flow", null, false, FlowInstallationMethod.Unknown)]
    public void InstallationDetector_UsesOnlyRecognizedEvidence(
        bool isWindows,
        string executableDirectory,
        string applicationBaseDirectory,
        string? registeredMsiPath,
        bool hasVersionFile,
        FlowInstallationMethod expected)
    {
        var result = DefaultInstallationMethodDetector.Detect(new InstallationDetectionContext(
            isWindows,
            executableDirectory,
            applicationBaseDirectory,
            registeredMsiPath,
            hasVersionFile));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void JsonWriter_IsDeterministicAndUsesStableTechnicalValues()
    {
        var result = new FlowUpdateCheckResult(
            FlowUpdateStatus.Available,
            UpdateChannel.Prerelease,
            "0.2.0-alpha.3",
            "0.2.0-alpha.4",
            FlowInstallationMethod.DotNetTool,
            new Uri("https://github.com/MardSilva/FlowEngineNet/releases/tag/v0.2.0-alpha.4"),
            "FlowEngineNet.Tool.0.2.0-alpha.4.nupkg",
            null);

        var first = FlowUpdateJsonWriter.Serialize(result);
        var second = FlowUpdateJsonWriter.Serialize(result);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(first);
        Assert.Equal("flow-update-check-0.1", document.RootElement.GetProperty("format").GetString());
        Assert.Equal("update-dotnet-tool", document.RootElement.GetProperty("recommendedAction").GetString());
        Assert.Equal(
            "dotnet tool update --global FlowEngineNet.Tool --version 0.2.0-alpha.4",
            document.RootElement.GetProperty("recommendedCommand").GetString());
    }

    [Fact]
    public async Task CliOperation_LocalizesHumanOutputAndDoesNotInstallAnything()
    {
        var checker = new FakeChecker(new FlowUpdateCheckResult(
            FlowUpdateStatus.Available,
            UpdateChannel.Stable,
            "0.2.0-alpha.3",
            "0.3.0",
            FlowInstallationMethod.Msi,
            new Uri("https://github.com/MardSilva/FlowEngineNet/releases/tag/v0.3.0"),
            "FlowEngineNet.Setup.0.3.0.pt-BR.win-x64.msi",
            new string('a', 64)));
        var operations = CreateOperations(checker);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await operations.ExecuteAsync(
            new UpdateCheckCommand(),
            output,
            error,
            new CliTextCatalog("pt-BR"));

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("há uma atualização disponível", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("O Flow não baixa nem inicia o instalador", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task OtherCliOperations_DoNotConsultReleaseSource()
    {
        var checker = new FakeChecker(new FlowUpdateCheckResult(
            FlowUpdateStatus.Current,
            UpdateChannel.Stable,
            "0.2.0",
            "0.2.0",
            FlowInstallationMethod.Unknown,
            FlowUpdateContract.ProjectReleasesPage,
            null,
            null));
        var operations = CreateOperations(checker);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await operations.ExecuteAsync(new HelpCommand(), output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, checker.CallCount);
    }

    [Theory]
    [InlineData("Network", "FLOWCLI_UPDATE_NETWORK")]
    [InlineData("Timeout", "FLOWCLI_UPDATE_TIMEOUT")]
    [InlineData("InvalidResponse", "FLOWCLI_UPDATE_INVALID_RESPONSE")]
    [InlineData("NoRelease", "FLOWCLI_UPDATE_NO_RELEASE")]
    public async Task CliOperation_ReturnsStableLocalizedDiagnostic(
        string kindName,
        string expectedCode)
    {
        var kind = Enum.Parse<UpdateCheckFailureKind>(kindName);
        var operations = CreateOperations(new FakeChecker(new UpdateCheckException(kind, "technical")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await operations.ExecuteAsync(
            new UpdateCheckCommand(),
            output,
            error,
            new CliTextCatalog("pt-BR"));

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.StartsWith(expectedCode + ":", error.ToString(), StringComparison.Ordinal);
    }

    private static FlowUpdateChecker CreateChecker(
        string currentVersion,
        FlowInstallationMethod method,
        params string[] releases) =>
        new(new FakeSource(Releases(releases)), new FakeDetector(method), currentVersion);

    private static CliOperations CreateOperations(IFlowUpdateChecker checker) =>
        new(
            new FlowJsonDocumentSerializer(),
            new EpubImporter(),
            new EpubPublicationInspector(),
            new DocumentValidator(),
            new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
            new AdaptiveLayoutEngine(),
            new HtmlDocumentRenderer(),
            updateChecker: checker);

    private static byte[] Releases(params string[] releases) =>
        Encoding.UTF8.GetBytes($"[{string.Join(',', releases)}]");

    private static string Release(
        string tag,
        bool prerelease,
        string? assetName = null,
        string? sha256 = null)
    {
        var asset = assetName is null
            ? string.Empty
            : $$"""
              {
                "name":"{{assetName}}",
                "browser_download_url":"https://github.com/MardSilva/FlowEngineNet/releases/download/{{tag}}/{{assetName}}"{{(sha256 is null ? string.Empty : $",\"digest\":\"sha256:{sha256}\"")}}
              }
              """;
        return $$"""
            {
              "tag_name":"{{tag}}",
              "draft":false,
              "prerelease":{{prerelease.ToString().ToLowerInvariant()}},
              "html_url":"https://github.com/MardSilva/FlowEngineNet/releases/tag/{{tag}}",
              "assets":[{{asset}}]
            }
            """;
    }

    private sealed class FakeSource(byte[] response) : IUpdateReleaseSource
    {
        private readonly byte[] _response = response;

        public int CallCount { get; private set; }

        public Task<byte[]> GetReleasesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeDetector(FlowInstallationMethod method) : IInstallationMethodDetector
    {
        public FlowInstallationMethod Detect() => method;
    }

    private sealed class FakeChecker : IFlowUpdateChecker
    {
        private readonly FlowUpdateCheckResult? _result;
        private readonly Exception? _exception;

        public FakeChecker(FlowUpdateCheckResult result) => _result = result;

        public FakeChecker(Exception exception) => _exception = exception;

        public int CallCount { get; private set; }

        public Task<FlowUpdateCheckResult> CheckAsync(
            UpdateChannel channel,
            string uiCultureName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<FlowUpdateCheckResult>(_exception);
        }
    }

    private sealed class RecordingHandler(byte[] response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(response),
            });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class RedirectingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://example.com/untrusted");
            return Task.FromResult(response);
        }
    }
}
