using System.Text.Json;

namespace Flow.Cli.Tests;

public sealed class CliExecutionLockTests
{
    [Fact]
    public void ActiveExecution_PreventsASecondProcessFromAcquiringTheDestination()
    {
        using var workspace = new TemporaryWorkspace();
        var destination = workspace.PathOf("gate.json");
        var first = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false));

        var second = CliExecutionLock.Acquire(destination, resume: false);
        var inspection = CliExecutionLock.Inspect(destination);

        Assert.False(second.IsSuccess);
        Assert.Equal("ErrorOutputExecutionActive", second.ErrorResourceKey);
        Assert.True(inspection.IsValid);
        Assert.True(inspection.IsWriterActive);
        Assert.Equal(first.ExecutionId, inspection.ExecutionId);
        Assert.Equal(CliExecutionRecordedState.Active, inspection.RecordedState);
        first.Complete();
        first.Dispose();
    }

    [Fact]
    public void InterruptedExecution_RequiresExplicitResumeAndReceivesANewIdentity()
    {
        using var workspace = new TemporaryWorkspace();
        var destination = workspace.PathOf("gate.json");
        var first = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false));
        var firstId = first.ExecutionId;
        var lockPath = first.LockPath;
        first.Dispose();

        var refused = CliExecutionLock.Acquire(destination, resume: false);
        var resumed = AssertAcquired(CliExecutionLock.Acquire(destination, resume: true));

        Assert.False(refused.IsSuccess);
        Assert.Equal("ErrorInterruptedExecutionRequiresResume", refused.ErrorResourceKey);
        Assert.NotEqual(firstId, resumed.ExecutionId);
        resumed.Complete();
        resumed.Dispose();
        Assert.Equal("completed", ReadState(lockPath));
    }

    [Fact]
    public void CompletedExecution_AllowsTheNextInvocationWithoutResume()
    {
        using var workspace = new TemporaryWorkspace();
        var destination = workspace.PathOf("corpus.json");
        using (var first = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false)))
        {
            first.Complete();
        }

        using var second = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false));

        Assert.NotEqual(Guid.Empty, second.ExecutionId);
        second.Complete();
    }

    [Fact]
    public void LockIdentity_DoesNotExposeThePhysicalDestination()
    {
        using var workspace = new TemporaryWorkspace();
        var destination = workspace.PathOf("private-book-gate.json");
        var executionLock = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false));
        var executionId = executionLock.ExecutionId;
        var lockPath = executionLock.LockPath;
        executionLock.Complete();
        executionLock.Dispose();

        var json = File.ReadAllText(lockPath);

        Assert.DoesNotContain(workspace.Root, json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("flow-cli-execution-lock-0.1", document.RootElement.GetProperty("format").GetString());
        Assert.Equal(64, document.RootElement.GetProperty("destinationSha256").GetString()!.Length);
        Assert.Equal(executionId.ToString("N"), document.RootElement.GetProperty("executionId").GetString());
    }

    [Fact]
    public void LockCopiedToAnotherDestination_IsPreservedAndRejected()
    {
        using var workspace = new TemporaryWorkspace();
        var firstDestination = workspace.PathOf("first.json");
        string firstLockPath;
        using (var first = AssertAcquired(CliExecutionLock.Acquire(firstDestination, resume: false)))
        {
            firstLockPath = first.LockPath;
            first.Complete();
        }

        var secondDestination = workspace.PathOf("second.json");
        var secondLockPath = workspace.PathOf(".second.json.flow-execution.lock");
        File.Copy(firstLockPath, secondLockPath);

        var result = CliExecutionLock.Acquire(secondDestination, resume: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("ErrorOutputLockInvalid", result.ErrorResourceKey);
        Assert.True(File.Exists(secondLockPath));
    }

    [Fact]
    public void Maintenance_RequiresExactExecutionIdAndExclusiveAccess()
    {
        using var workspace = new TemporaryWorkspace();
        var destination = workspace.PathOf("review");
        var active = AssertAcquired(CliExecutionLock.Acquire(destination, resume: false));
        var id = active.ExecutionId;

        var whileActive = CliExecutionLock.AcquireForMaintenance(destination, id);
        active.Dispose();
        var wrongId = CliExecutionLock.AcquireForMaintenance(destination, Guid.NewGuid());
        using var maintenance = AssertAcquired(CliExecutionLock.AcquireForMaintenance(destination, id));

        Assert.False(whileActive.IsSuccess);
        Assert.Equal("ErrorOutputExecutionActive", whileActive.ErrorResourceKey);
        Assert.False(wrongId.IsSuccess);
        Assert.Equal("ErrorExecutionIdMismatch", wrongId.ErrorResourceKey);
        maintenance.Complete();
    }

    private static CliExecutionLock AssertAcquired(CliExecutionLockAcquisition acquisition)
    {
        Assert.True(acquisition.IsSuccess);
        return Assert.IsType<CliExecutionLock>(acquisition.ExecutionLock);
    }

    private static string ReadState(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("state").GetString()!;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-cli-lock-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
