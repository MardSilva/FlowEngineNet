using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Flow.Cli;

internal sealed record CliExecutionStatusReport(
    CliExecutionInspection Lock,
    CliOutputArtifactInspection Artifacts,
    bool OutputFileExists,
    bool OutputDirectoryExists);

internal static class CliExecutionStatusReportJsonSerializer
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Serialize(CliExecutionStatusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "flow-cli-execution-status-0.1");
            writer.WriteStartObject("lock");
            writer.WriteBoolean("exists", report.Lock.Exists);
            writer.WriteBoolean("valid", report.Lock.IsValid);
            writer.WriteBoolean("writerActive", report.Lock.IsWriterActive);
            writer.WriteString("executionId", report.Lock.ExecutionId?.ToString("N"));
            writer.WriteString("recordedState", Token(report.Lock.RecordedState));
            writer.WriteEndObject();
            writer.WriteStartObject("output");
            writer.WriteBoolean("fileExists", report.OutputFileExists);
            writer.WriteBoolean("directoryExists", report.OutputDirectoryExists);
            writer.WriteEndObject();
            writer.WriteStartObject("artifacts");
            writer.WriteNumber("temporaryFiles", report.Artifacts.TemporaryFiles);
            writer.WriteNumber("stagingDirectories", report.Artifacts.StagingDirectories);
            writer.WriteNumber("backupDirectories", report.Artifacts.BackupDirectories);
            writer.WriteNumber("total", report.Artifacts.Total);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    public static async Task WriteAtomicallyAsync(
        CliExecutionStatusReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The execution-status report must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, Serialize(report), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? Token(CliExecutionRecordedState? state) => state switch
    {
        CliExecutionRecordedState.Active => "active",
        CliExecutionRecordedState.Completed => "completed",
        CliExecutionRecordedState.Interrupted => "interrupted",
        _ => null,
    };

    private static byte[] Normalize(ReadOnlySpan<byte> json)
    {
        var value = Utf8WithoutBom.GetString(json).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Utf8WithoutBom.GetBytes(value + "\n");
    }
}
