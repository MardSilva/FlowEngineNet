using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flow.Epub.Corpus;

/// <summary>Runs a trusted local EPUBCheck installation with bounded I/O and no shell.</summary>
public sealed partial class EpubCheckProcessAdapter : IEpubCheckAdapter
{
    private readonly EpubCheckProcessOptions? options;
    private readonly Func<string> createTemporaryDirectory;

    public EpubCheckProcessAdapter(EpubCheckProcessOptions? options = null)
        : this(options, static () => Directory.CreateTempSubdirectory("flow-epubcheck-").FullName)
    {
    }

    internal EpubCheckProcessAdapter(EpubCheckProcessOptions? options, Func<string> createTemporaryDirectory)
    {
        this.options = options;
        this.createTemporaryDirectory = createTemporaryDirectory;
    }

    public async Task<EpubCheckEvidence> EvaluateAsync(
        Stream epub,
        string logicalPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(epub);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPublicationId);
        if (options is null)
        {
            return new EpubCheckEvidence(EpubCheckEvidenceStatus.Disabled);
        }

        var executable = options.ToolKind == EpubCheckToolKind.Jar ? options.JavaExecutablePath! : options.ToolPath;
        if (!File.Exists(executable) || !File.Exists(options.ToolPath))
        {
            return Failure(EpubCheckEvidenceStatus.Unavailable, EpubCheckDiagnosticCodes.ToolMissing,
                "The configured EPUBCheck tool is not available.");
        }

        var temporaryDirectory = createTemporaryDirectory();
        try
        {
            var epubPath = Path.Combine(temporaryDirectory, "publication.epub");
            await using (var destination = new FileStream(epubPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await epub.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            var versionRun = await RunAsync(["--version"], epubPath, cancellationToken).ConfigureAwait(false);
            if (versionRun.TimedOut)
            {
                return Failure(EpubCheckEvidenceStatus.TimedOut, EpubCheckDiagnosticCodes.Timeout,
                    "EPUBCheck timed out while reporting its version.");
            }

            if (versionRun.OutputTruncated)
            {
                return OutputLimit(versionRun);
            }

            var version = ParseVersion(versionRun.StandardOutput + "\n" + versionRun.StandardError);
            if (version is null)
            {
                return Failure(EpubCheckEvidenceStatus.UnrecognizedOutput, EpubCheckDiagnosticCodes.UnrecognizedOutput,
                    "EPUBCheck version output was not recognized.", versionRun);
            }

            if (version.Major < options.MinimumSupportedMajorVersion || version.Major > options.MaximumSupportedMajorVersion)
            {
                return Failure(EpubCheckEvidenceStatus.IncompatibleVersion, EpubCheckDiagnosticCodes.IncompatibleVersion,
                    $"EPUBCheck major version {version.Major} is outside the configured supported range.", versionRun, version.ToString());
            }

            var validationRun = await RunAsync(["--json", "-", epubPath], epubPath, cancellationToken).ConfigureAwait(false);
            if (validationRun.TimedOut)
            {
                return Failure(EpubCheckEvidenceStatus.TimedOut, EpubCheckDiagnosticCodes.Timeout,
                    "EPUBCheck timed out while checking the publication.", validationRun, version.ToString());
            }

            if (validationRun.OutputTruncated)
            {
                return OutputLimit(validationRun, version.ToString());
            }

            if (!TryParseReport(validationRun.StandardOutput, out var report))
            {
                return Failure(EpubCheckEvidenceStatus.UnrecognizedOutput, EpubCheckDiagnosticCodes.UnrecognizedOutput,
                    "EPUBCheck JSON output was not recognized.", validationRun, version.ToString());
            }

            if (validationRun.ExitCode is not 0 and not 1)
            {
                return Failure(EpubCheckEvidenceStatus.ToolError, EpubCheckDiagnosticCodes.ToolFailure,
                    "EPUBCheck exited without producing a conformance result.", validationRun, version.ToString());
            }

            var status = validationRun.ExitCode == 1 || report.FatalCount > 0 || report.ErrorCount > 0
                ? EpubCheckEvidenceStatus.NonConformant
                : EpubCheckEvidenceStatus.Conformant;
            return new EpubCheckEvidence(
                status,
                report.ToolVersion ?? version.ToString(),
                validationRun.ExitCode,
                report.FatalCount,
                report.ErrorCount,
                report.WarningCount,
                report.UsageCount,
                report.Messages,
                standardOutput: validationRun.StandardOutput,
                standardError: validationRun.StandardError);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private async Task<ProcessRun> RunAsync(
        IReadOnlyList<string> invocationArguments,
        string epubPath,
        CancellationToken cancellationToken)
    {
        var executable = options!.ToolKind == EpubCheckToolKind.Jar ? options.JavaExecutablePath! : options.ToolPath;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (options.ToolKind == EpubCheckToolKind.Jar)
        {
            start.ArgumentList.Add("-jar");
            start.ArgumentList.Add(options.ToolPath);
        }

        foreach (var argument in options.ToolArguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var argument in invocationArguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            return new ProcessRun(-1, string.Empty, string.Empty, false, false);
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, options.MaximumCapturedCharacters);
        var stderrTask = ReadBoundedAsync(process.StandardError, options.MaximumCapturedCharacters);
        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            timedOut = true;
            Kill(process);
        }
        catch
        {
            Kill(process);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRun(
            process.HasExited ? process.ExitCode : -1,
            Sanitize(stdout.Text, epubPath),
            Sanitize(stderr.Text, epubPath),
            stdout.Truncated || stderr.Truncated,
            timedOut);
    }

    private static async Task<CapturedText> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var remaining = maximumCharacters - result.Length;
            if (remaining > 0)
            {
                result.Append(buffer, 0, Math.Min(read, remaining));
            }

            truncated |= read > remaining;
        }

        return new CapturedText(result.ToString(), truncated);
    }

    private static bool TryParseReport(string json, out ParsedReport report)
    {
        report = default!;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("checker", out var checker)
                || checker.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsedMessages = messages.EnumerateArray().Take(1000).Select(ParseMessage).ToArray();
            report = new ParsedReport(
                GetInt(checker, "nFatal"),
                GetInt(checker, "nError"),
                GetInt(checker, "nWarning"),
                GetInt(checker, "nUsage"),
                GetString(checker, "checkerVersion"),
                parsedMessages);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static EpubCheckMessageEvidence ParseMessage(JsonElement message)
    {
        var resource = message.TryGetProperty("locations", out var locations)
                       && locations.ValueKind == JsonValueKind.Array
                       && locations.GetArrayLength() > 0
                       && locations[0].TryGetProperty("path", out var path)
            ? SanitizeResource(path.GetString())
            : null;
        return new EpubCheckMessageEvidence(
            GetString(message, "ID") ?? "UNKNOWN",
            GetString(message, "severity") ?? "unknown",
            GetString(message, "message") ?? "EPUBCheck reported a message without text.",
            resource);
    }

    private static EpubCheckEvidence Failure(
        EpubCheckEvidenceStatus status,
        string code,
        string message,
        ProcessRun? run = null,
        string? version = null) => new(
        status,
        version,
        run?.ExitCode,
        diagnostics:
        [
            new EpubCorpusExecutionDiagnostic(
                code,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.ExternalConformance,
                message),
        ],
        standardOutput: run?.StandardOutput,
        standardError: run?.StandardError);

    private static EpubCheckEvidence OutputLimit(ProcessRun run, string? version = null) => Failure(
        EpubCheckEvidenceStatus.OutputLimitExceeded,
        EpubCheckDiagnosticCodes.OutputLimitExceeded,
        "EPUBCheck output exceeded the configured capture limit.",
        run,
        version);

    private static Version? ParseVersion(string value)
    {
        var match = VersionPattern().Match(value);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    private string Sanitize(string value, string epubPath)
    {
        var workspace = Path.GetDirectoryName(epubPath)!;
        var replacements = new[]
        {
            (epubPath, "<epub>"),
            (workspace, "<workspace>"),
            (options!.ToolPath, "<tool>"),
            (options.JavaExecutablePath, "<java>"),
        };
        foreach (var (path, marker) in replacements)
        {
            if (path is null)
            {
                continue;
            }

            value = value.Replace(path, marker, StringComparison.OrdinalIgnoreCase);
            var jsonEscaped = JsonSerializer.Serialize(path)[1..^1];
            value = value.Replace(jsonEscaped, marker, StringComparison.OrdinalIgnoreCase);
        }

        value = WindowsAbsolutePathPattern().Replace(value, "<local-path>");
        return UnixAbsolutePathPattern().Replace(value, "<local-path>");
    }

    private static string? SanitizeResource(string? value) => string.IsNullOrWhiteSpace(value)
        || Path.IsPathFullyQualified(value)
        || Uri.TryCreate(value, UriKind.Absolute, out _)
            ? null
            : value.Replace('\\', '/');

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [GeneratedRegex(@"EPUBCheck\s+v?(\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("""[A-Za-z]:[\\/](?:[^\r\n"']+)""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WindowsAbsolutePathPattern();

    [GeneratedRegex("""(?<![:\w])/(?:[^/\s"']+/)+[^\s"']+""", RegexOptions.CultureInvariant, 100)]
    private static partial Regex UnixAbsolutePathPattern();

    private sealed record CapturedText(string Text, bool Truncated);

    private sealed record ProcessRun(int ExitCode, string StandardOutput, string StandardError, bool OutputTruncated, bool TimedOut);

    private sealed record ParsedReport(
        int FatalCount,
        int ErrorCount,
        int WarningCount,
        int UsageCount,
        string? ToolVersion,
        IReadOnlyList<EpubCheckMessageEvidence> Messages);
}
