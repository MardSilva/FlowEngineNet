using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;

namespace Flow.Updates;

/// <summary>Identifies a recognized local Flow distribution mechanism.</summary>
public enum FlowInstallationMethod
{
    Unknown,
    Msi,
    DotNetTool,
    Portable,
}

/// <summary>Describes whether the selected release channel contains a newer version.</summary>
public enum FlowUpdateStatus
{
    Current,
    Available,
}

/// <summary>Contains validated, non-installing update information from the official release source.</summary>
public sealed record FlowUpdateCheckResult(
    FlowUpdateStatus Status,
    UpdateChannel Channel,
    string CurrentVersion,
    string LatestVersion,
    FlowInstallationMethod InstallationMethod,
    Uri ReleaseUrl,
    string? ArtifactName,
    string? ArtifactSha256);

/// <summary>Checks the configured official release source without downloading or installing artifacts.</summary>
public interface IFlowUpdateChecker
{
    public Task<FlowUpdateCheckResult> CheckAsync(
        UpdateChannel channel,
        string uiCultureName,
        CancellationToken cancellationToken = default);
}

internal interface IUpdateReleaseSource
{
    public Task<byte[]> GetReleasesAsync(CancellationToken cancellationToken);
}

public enum UpdateCheckFailureKind
{
    Network,
    Timeout,
    InvalidResponse,
    NoRelease,
}

public sealed class UpdateCheckException(UpdateCheckFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UpdateCheckFailureKind Kind { get; } = kind;
}

internal sealed class HttpUpdateReleaseSource : IUpdateReleaseSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public HttpUpdateReleaseSource()
        : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), ownsClient: true)
    {
    }

    internal HttpUpdateReleaseSource(HttpClient client, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        if (!_client.DefaultRequestHeaders.UserAgent.Any())
        {
            _client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(FlowUpdateContract.UserAgentProduct, UpdateProductInfo.Version));
        }

        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<byte[]> GetReleasesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FlowUpdateContract.RequestTimeout);
        try
        {
            using var response = await _client.GetAsync(
                    FlowUpdateContract.ReleasesEndpoint,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } finalUri
                && finalUri != FlowUpdateContract.ReleasesEndpoint)
            {
                throw new UpdateCheckException(
                    UpdateCheckFailureKind.InvalidResponse,
                    "The release request was redirected away from the configured endpoint.");
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw new UpdateCheckException(
                    UpdateCheckFailureKind.InvalidResponse,
                    "The release endpoint returned a redirect.");
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > FlowUpdateContract.MaximumResponseBytes)
            {
                throw new UpdateCheckException(
                    UpdateCheckFailureKind.InvalidResponse,
                    "The release response exceeds the configured size limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > FlowUpdateContract.MaximumResponseBytes)
                {
                    throw new UpdateCheckException(
                        UpdateCheckFailureKind.InvalidResponse,
                        "The release response exceeds the configured size limit.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateCheckException(UpdateCheckFailureKind.Timeout, "The release request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UpdateCheckException(UpdateCheckFailureKind.Network, "The release request failed.", exception);
        }
        catch (IOException exception)
        {
            throw new UpdateCheckException(UpdateCheckFailureKind.Network, "The release response was interrupted.", exception);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}

internal sealed record InstallationDetectionContext(
    bool IsWindows,
    string ExecutableDirectory,
    string ApplicationBaseDirectory,
    string? RegisteredMsiInstallPath,
    bool HasPortableVersionFile,
    bool IsGraphicalApplication = false);

internal interface IInstallationMethodDetector
{
    public FlowInstallationMethod Detect();
}

internal sealed class DefaultInstallationMethodDetector(bool graphicalApplication = false) : IInstallationMethodDetector
{
    public FlowInstallationMethod Detect()
    {
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        var registeredPath = ReadMsiInstallPath();
        return Detect(new InstallationDetectionContext(
            OperatingSystem.IsWindows(),
            executableDirectory,
            AppContext.BaseDirectory,
            registeredPath,
            File.Exists(Path.Combine(executableDirectory, "VERSION.json")),
            graphicalApplication));
    }

    internal static FlowInstallationMethod Detect(InstallationDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsWindows
            && !string.IsNullOrWhiteSpace(context.RegisteredMsiInstallPath)
            && (PathsEqual(context.RegisteredMsiInstallPath, context.ExecutableDirectory, windows: true)
                || (context.IsGraphicalApplication && PathsEqual(
                    context.RegisteredMsiInstallPath.TrimEnd('\\', '/') + "/app",
                    context.ExecutableDirectory, windows: true))))
        {
            return FlowInstallationMethod.Msi;
        }

        var normalizedBaseDirectory = context.ApplicationBaseDirectory.Replace('\\', '/');
        if (normalizedBaseDirectory.Contains(
                "/.store/flowenginenet.tool/",
                StringComparison.OrdinalIgnoreCase))
        {
            return FlowInstallationMethod.DotNetTool;
        }

        return context.HasPortableVersionFile
            ? FlowInstallationMethod.Portable
            : FlowInstallationMethod.Unknown;
    }

    private static string? ReadMsiInstallPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\FlowEngineNet\Installer");
            return key?.GetValue("InstallPath") as string;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right, bool windows)
    {
        try
        {
            if (windows)
            {
                return string.Equals(
                    NormalizeWindowsPath(left),
                    NormalizeWindowsPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeWindowsPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (OperatingSystem.IsWindows())
        {
            normalized = Path.GetFullPath(normalized);
        }

        var rootLength = normalized.StartsWith("\\\\", StringComparison.Ordinal)
            ? 2
            : normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '\\'
                ? 3
                : 0;
        return normalized.Length > rootLength
            ? normalized.TrimEnd('\\')
            : normalized;
    }
}

internal sealed class FlowUpdateChecker(
    IUpdateReleaseSource source,
    IInstallationMethodDetector installationMethodDetector,
    string currentVersion) : IFlowUpdateChecker
{
    private readonly IUpdateReleaseSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IInstallationMethodDetector _installationMethodDetector = installationMethodDetector
        ?? throw new ArgumentNullException(nameof(installationMethodDetector));
    private readonly string _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));

    public static FlowUpdateChecker CreateDefault() =>
        new(new HttpUpdateReleaseSource(), new DefaultInstallationMethodDetector(), UpdateProductInfo.Version);

    public async Task<FlowUpdateCheckResult> CheckAsync(
        UpdateChannel channel,
        string uiCultureName,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uiCultureName);
        if (!SemanticVersion.TryParse(_currentVersion, out var current))
        {
            throw InvalidResponse("The installed Flow version is not valid SemVer.");
        }

        var bytes = await _source.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var releases = ParseReleases(bytes);
        var candidates = releases
            .Where(release => !release.Draft)
            .Where(release => channel == UpdateChannel.Prerelease || !release.Prerelease)
            .Where(release => channel == UpdateChannel.Prerelease || !release.Version.IsPrerelease)
            .OrderByDescending(release => release.Version)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new UpdateCheckException(
                UpdateCheckFailureKind.NoRelease,
                "The release response contains no release for the selected channel.");
        }

        var latest = candidates[0];
        var method = _installationMethodDetector.Detect();
        var asset = SelectArtifact(latest.Assets, method, uiCultureName);
        return new FlowUpdateCheckResult(
            latest.Version.CompareTo(current) > 0 ? FlowUpdateStatus.Available : FlowUpdateStatus.Current,
            channel,
            current!.Value,
            latest.Version.Value,
            method,
            latest.PageUrl,
            asset?.Name,
            asset?.Sha256);
    }

    private static ImmutableArray<Release> ParseReleases(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse("The release response root must be an array.");
            }

            var releases = ImmutableArray.CreateBuilder<Release>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryRequiredString(item, "tag_name", out var tag)
                    || !TryRequiredBoolean(item, "draft", out var draft)
                    || !TryRequiredBoolean(item, "prerelease", out var prerelease)
                    || !TryRequiredString(item, "html_url", out var pageUrlText)
                    || !SemanticVersion.TryParse(tag, out var version)
                    || !TryOfficialReleasePage(pageUrlText, out var pageUrl))
                {
                    throw InvalidResponse("A release entry contains invalid identity or version data.");
                }

                var assets = ParseAssets(item);
                releases.Add(new Release(version!, draft, prerelease, pageUrl!, assets));
            }

            return releases.ToImmutable();
        }
        catch (JsonException exception)
        {
            throw new UpdateCheckException(
                UpdateCheckFailureKind.InvalidResponse,
                "The release response is not valid JSON.",
                exception);
        }
    }

    private static ImmutableArray<ReleaseAsset> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("A release entry has no valid assets array.");
        }

        var result = ImmutableArray.CreateBuilder<ReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !TryRequiredString(asset, "name", out var name)
                || !TryRequiredString(asset, "browser_download_url", out var downloadText)
                || !TryOfficialAssetUrl(downloadText, out _))
            {
                throw InvalidResponse("A release asset contains an invalid name or URL.");
            }

            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind != JsonValueKind.Null)
            {
                if (digest.ValueKind != JsonValueKind.String
                    || !TryParseSha256Digest(digest.GetString(), out sha256))
                {
                    throw InvalidResponse("A release asset contains an invalid digest.");
                }
            }

            result.Add(new ReleaseAsset(name, sha256));
        }

        return result.ToImmutable();
    }

    private static ReleaseAsset? SelectArtifact(
        ImmutableArray<ReleaseAsset> assets,
        FlowInstallationMethod method,
        string uiCultureName)
    {
        var suffix = method switch
        {
            FlowInstallationMethod.Msi when uiCultureName.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) =>
                ".pt-BR.win-x64.msi",
            FlowInstallationMethod.Msi => ".en-US.win-x64.msi",
            FlowInstallationMethod.DotNetTool => ".nupkg",
            FlowInstallationMethod.Portable => ".win-x64.zip",
            _ => null,
        };
        return suffix is null
            ? null
            : assets.FirstOrDefault(asset => asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryRequiredString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static bool TryRequiredBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryOfficialReleasePage(string value, out Uri? uri) =>
        TryOfficialUri(value, "/MardSilva/FlowEngineNet/releases/", out uri);

    private static bool TryOfficialAssetUrl(string value, out Uri? uri) =>
        TryOfficialUri(value, "/MardSilva/FlowEngineNet/releases/download/", out uri);

    private static bool TryOfficialUri(string value, string pathPrefix, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.IsDefaultPort || !string.IsNullOrEmpty(parsed.UserInfo)
            || !parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !parsed.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool TryParseSha256Digest(string? value, out string? sha256)
    {
        sha256 = null;
        if (value is null || !value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = value[7..];
        if (candidate.Length != 64 || candidate.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        sha256 = candidate.ToLowerInvariant();
        return true;
    }

    private static UpdateCheckException InvalidResponse(string message) =>
        new(UpdateCheckFailureKind.InvalidResponse, message);

    private sealed record Release(
        SemanticVersion Version,
        bool Draft,
        bool Prerelease,
        Uri PageUrl,
        ImmutableArray<ReleaseAsset> Assets);

    private sealed record ReleaseAsset(string Name, string? Sha256);
}
