using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Windows.Shell;

/// <summary>Stores shell preferences in a bounded local JSON file using atomic replacement.</summary>
public sealed class JsonFlowWindowsSettingsStore : IFlowWindowsSettingsStore
{
    public const string FileName = "settings.json";
    private const int MaximumSettingsBytes = 16 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _settingsPath;

    public JsonFlowWindowsSettingsStore(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _settingsPath = Path.Combine(Path.GetFullPath(applicationDataDirectory), FileName);
    }

    public string SettingsPath => _settingsPath;

    public async Task<FlowWindowsSettings> LoadAsync(
        FlowWindowsSettings fallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (!File.Exists(_settingsPath))
        {
            return fallback;
        }

        try
        {
            var info = new FileInfo(_settingsPath);
            if (info.Length is <= 0 or > MaximumSettingsBytes)
            {
                return fallback;
            }

            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return document is not null
                   && document.Format == SettingsDocument.CurrentFormat
                   && FlowWindowsSettings.IsSupportedLanguage(document.Language)
                   && Enum.IsDefined(document.Theme)
                ? CreateSettings(document, fallback)
                : fallback;
        }
        catch (Exception exception) when (exception is JsonException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException)
        {
            return fallback;
        }
    }

    public async Task SaveAsync(FlowWindowsSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_settingsPath)
                        ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        new SettingsDocument(
                            settings.Language,
                            settings.Theme,
                            settings.AdvancedMode,
                            settings.PersonalLibraryPath),
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record SettingsDocument(
        string? Language,
        FlowWindowsTheme Theme,
        bool AdvancedMode,
        string? PersonalLibraryPath = null)
    {
        public const string CurrentFormat = "flow-windows-settings-0.1";

        public string Format { get; init; } = CurrentFormat;
    }

    private static FlowWindowsSettings CreateSettings(
        SettingsDocument document,
        FlowWindowsSettings fallback)
    {
        try
        {
            return new FlowWindowsSettings(
                document.Language!,
                document.Theme,
                document.AdvancedMode,
                document.PersonalLibraryPath);
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }
}
