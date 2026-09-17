using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flow.Cli;

internal enum CliExecutionRecordedState
{
    Active,
    Completed,
    Interrupted,
}

internal sealed record CliExecutionInspection(
    bool Exists,
    bool IsValid,
    bool IsWriterActive,
    Guid? ExecutionId,
    CliExecutionRecordedState? RecordedState,
    string LockPath,
    string? ErrorResourceKey = null);

internal sealed record CliExecutionLockAcquisition(
    bool IsSuccess,
    CliExecutionLock? ExecutionLock,
    string? ErrorResourceKey = null)
{
    public static CliExecutionLockAcquisition Acquired(CliExecutionLock executionLock) =>
        new(true, executionLock);

    public static CliExecutionLockAcquisition Failure(string resourceKey) =>
        new(false, null, resourceKey);
}

/// <summary>Coordinates one output destination across CLI processes and records recoverable execution identity.</summary>
internal sealed class CliExecutionLock : IDisposable
{
    private const string CurrentFormat = "flow-cli-execution-lock-0.1";
    private const int MaximumLockBytes = 4096;
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly FileStream stream;
    private readonly string destinationHash;
    private readonly bool markInterruptedOnDispose;
    private bool completed;
    private bool disposed;

    private CliExecutionLock(
        FileStream stream,
        string destinationHash,
        Guid executionId,
        bool markInterruptedOnDispose = true)
    {
        this.stream = stream;
        this.destinationHash = destinationHash;
        this.markInterruptedOnDispose = markInterruptedOnDispose;
        ExecutionId = executionId;
    }

    public Guid ExecutionId { get; }

    public string LockPath => stream.Name;

    public static CliExecutionLockAcquisition Acquire(string destinationPath, bool resume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output destination must have a parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(parent);
        EnsureSafeDirectory(parent);
        var lockPath = Path.Combine(parent, $".{Path.GetFileName(destination)}.flow-execution.lock");
        var destinationHash = HashDestination(NormalizeDestinationForHash(destination));

        FileStream stream;
        var created = false;
        try
        {
            stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            created = true;
        }
        catch (IOException)
        {
            try
            {
                stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (IOException)
            {
                return CliExecutionLockAcquisition.Failure("ErrorOutputExecutionActive");
            }
        }

        try
        {
            if (!created)
            {
                if (File.GetAttributes(lockPath).HasFlag(FileAttributes.ReparsePoint))
                {
                    return FailureAndDispose(stream, "ErrorOutputLockInvalid");
                }

                LockRecord? record;
                try
                {
                    record = ReadRecord(stream, destinationHash);
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    return FailureAndDispose(stream, "ErrorOutputLockInvalid");
                }
                if (record is null)
                {
                    return FailureAndDispose(stream, "ErrorOutputLockInvalid");
                }

                if (record.State != CliExecutionRecordedState.Completed && !resume)
                {
                    return FailureAndDispose(stream, "ErrorInterruptedExecutionRequiresResume");
                }
            }

            var executionId = Guid.NewGuid();
            WriteState(stream, destinationHash, executionId, "active");
            return CliExecutionLockAcquisition.Acquired(new CliExecutionLock(stream, destinationHash, executionId));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static CliExecutionInspection Inspect(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
        var lockPath = GetLockPath(destination);
        if (!File.Exists(lockPath))
        {
            return new CliExecutionInspection(false, true, false, null, null, lockPath);
        }

        if (File.GetAttributes(lockPath).HasFlag(FileAttributes.ReparsePoint))
        {
            return new CliExecutionInspection(true, false, false, null, null, lockPath, "ErrorOutputLockInvalid");
        }

        var destinationHash = HashDestination(NormalizeDestinationForHash(destination));
        LockRecord? record;
        try
        {
            using var reader = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            record = ReadRecord(reader, destinationHash);
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new CliExecutionInspection(true, false, false, null, null, lockPath, "ErrorOutputLockInvalid");
        }

        if (record is null)
        {
            return new CliExecutionInspection(true, false, false, null, null, lockPath, "ErrorOutputLockInvalid");
        }

        var writerActive = false;
        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            writerActive = true;
        }

        var state = record.State == CliExecutionRecordedState.Active && !writerActive
            ? CliExecutionRecordedState.Interrupted
            : record.State;
        return new CliExecutionInspection(true, true, writerActive, record.ExecutionId, state, lockPath);
    }

    public static CliExecutionLockAcquisition AcquireForMaintenance(string destinationPath, Guid expectedExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (expectedExecutionId == Guid.Empty)
        {
            throw new ArgumentException("The expected execution ID cannot be empty.", nameof(expectedExecutionId));
        }

        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
        var lockPath = GetLockPath(destination);
        if (!File.Exists(lockPath))
        {
            return CliExecutionLockAcquisition.Failure("ErrorExecutionLockMissing");
        }

        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return CliExecutionLockAcquisition.Failure("ErrorOutputExecutionActive");
        }

        try
        {
            if (File.GetAttributes(lockPath).HasFlag(FileAttributes.ReparsePoint))
            {
                return FailureAndDispose(stream, "ErrorOutputLockInvalid");
            }

            var destinationHash = HashDestination(NormalizeDestinationForHash(destination));
            LockRecord? record;
            try
            {
                record = ReadRecord(stream, destinationHash);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return FailureAndDispose(stream, "ErrorOutputLockInvalid");
            }

            if (record is null)
            {
                return FailureAndDispose(stream, "ErrorOutputLockInvalid");
            }

            if (record.ExecutionId != expectedExecutionId)
            {
                return FailureAndDispose(stream, "ErrorExecutionIdMismatch");
            }

            return CliExecutionLockAcquisition.Acquired(
                new CliExecutionLock(stream, destinationHash, expectedExecutionId, markInterruptedOnDispose: false));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            return;
        }

        WriteState(stream, destinationHash, ExecutionId, "completed");
        completed = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (!completed && markInterruptedOnDispose)
            {
                try
                {
                    WriteState(stream, destinationHash, ExecutionId, "interrupted");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best effort: an unchanged active state is still treated as interrupted after the handle closes.
                }
            }
        }
        finally
        {
            disposed = true;
            stream.Dispose();
        }
    }

    private static CliExecutionLockAcquisition FailureAndDispose(FileStream stream, string resourceKey)
    {
        stream.Dispose();
        return CliExecutionLockAcquisition.Failure(resourceKey);
    }

    private static LockRecord? ReadRecord(Stream stream, string expectedDestinationHash)
    {
        if (stream.Length is <= 0 or > MaximumLockBytes)
        {
            return null;
        }

        stream.Position = 0;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("format").GetString() != CurrentFormat
            || root.GetProperty("destinationSha256").GetString() != expectedDestinationHash
            || !Guid.TryParseExact(root.GetProperty("executionId").GetString(), "N", out _))
        {
            return null;
        }

        var executionId = Guid.ParseExact(root.GetProperty("executionId").GetString()!, "N");
        var state = root.GetProperty("state").GetString() switch
        {
            "active" => CliExecutionRecordedState.Active,
            "completed" => CliExecutionRecordedState.Completed,
            "interrupted" => CliExecutionRecordedState.Interrupted,
            _ => (CliExecutionRecordedState?)null,
        };
        return state is null ? null : new LockRecord(executionId, state.Value);
    }

    private static void WriteState(FileStream stream, string destinationHash, Guid executionId, string state)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", CurrentFormat);
            writer.WriteString("destinationSha256", destinationHash);
            writer.WriteString("executionId", executionId.ToString("N"));
            writer.WriteString("state", state);
            writer.WriteEndObject();
            writer.Flush();
        }

        var bytes = Normalize(buffer.WrittenSpan);
        stream.Position = 0;
        stream.SetLength(0);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static byte[] Normalize(ReadOnlySpan<byte> json)
    {
        var text = Utf8WithoutBom.GetString(json).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Utf8WithoutBom.GetBytes(text + "\n");
    }

    private static string NormalizeDestinationForHash(string path)
    {
        return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    }

    private static string HashDestination(string destination) =>
        Convert.ToHexString(SHA256.HashData(Utf8WithoutBom.GetBytes(destination)));

    private static string GetLockPath(string destination) =>
        Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.flow-execution.lock");

    private static void EnsureSafeDirectory(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The output lock path contains an unsafe reparse point.");
            }
        }
    }

    private sealed record LockRecord(Guid ExecutionId, CliExecutionRecordedState State);
}
