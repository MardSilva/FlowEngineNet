using System.Text;

namespace Flow.Cli.Tests;

public sealed class CliOutputPolicyTests
{
    [Fact]
    public void FileOutput_RequiresForceBeforeReplacingCompletedOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("gate.json");
        File.WriteAllText(output, "previous", Encoding.UTF8);

        var refused = CliOutputPolicy.PrepareFile(output, force: false, resume: false);
        var allowed = CliOutputPolicy.PrepareFile(output, force: true, resume: false);

        Assert.False(refused.IsSuccess);
        Assert.Equal("ErrorOutputExistsRequiresForce", refused.ErrorResourceKey);
        Assert.True(allowed.IsSuccess);
        Assert.Equal("previous", File.ReadAllText(output));
    }

    [Fact]
    public void FileOutput_ResumeRemovesOnlyRecognizedTemporaryArtifacts()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("gate.json");
        var interrupted = workspace.PathOf($".gate.json.{Guid.NewGuid():N}.tmp");
        var unrelated = workspace.PathOf(".gate.json.not-a-transaction.tmp");
        File.WriteAllText(interrupted, "partial", Encoding.UTF8);
        File.WriteAllText(unrelated, "keep", Encoding.UTF8);

        var refused = CliOutputPolicy.PrepareFile(output, force: false, resume: false);
        var resumed = CliOutputPolicy.PrepareFile(output, force: false, resume: true);

        Assert.False(refused.IsSuccess);
        Assert.Equal("ErrorInterruptedOutputRequiresResume", refused.ErrorResourceKey);
        Assert.True(resumed.IsSuccess);
        Assert.True(resumed.Resumed);
        Assert.False(File.Exists(interrupted));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void ReviewOutput_RequiresForceForCompletedDestination()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("review");
        Directory.CreateDirectory(output);

        var refused = CliOutputPolicy.PrepareReviewDirectory(output, force: false, resume: false);
        var allowed = CliOutputPolicy.PrepareReviewDirectory(output, force: true, resume: false);

        Assert.False(refused.IsSuccess);
        Assert.Equal("ErrorOutputExistsRequiresForce", refused.ErrorResourceKey);
        Assert.True(allowed.IsSuccess);
        Assert.True(Directory.Exists(output));
    }

    [Fact]
    public void ReviewOutput_ResumeDiscardsStagingAndRestoresRecognizedBackup()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("review");
        var staging = workspace.PathOf($".review.{Guid.NewGuid():N}.staging");
        var backup = workspace.PathOf($".review.{Guid.NewGuid():N}.backup");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(staging, "partial.txt"), "partial", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(backup, "review-manifest.json"),
            "{\"format\":\"flow-epub-large-review-manifest-0.1\"}",
            new UTF8Encoding(false));

        var refused = CliOutputPolicy.PrepareReviewDirectory(output, force: false, resume: false);
        var resumed = CliOutputPolicy.PrepareReviewDirectory(output, force: true, resume: true);

        Assert.False(refused.IsSuccess);
        Assert.Equal("ErrorInterruptedOutputRequiresResume", refused.ErrorResourceKey);
        Assert.True(resumed.IsSuccess);
        Assert.True(resumed.Resumed);
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(backup));
        Assert.True(File.Exists(Path.Combine(output, "review-manifest.json")));
    }

    [Fact]
    public void ReviewOutput_ResumeDoesNotReplaceCompletedDestinationWithoutForce()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("review");
        var staging = workspace.PathOf($".review.{Guid.NewGuid():N}.staging");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(output, "keep.txt"), "keep", Encoding.UTF8);
        File.WriteAllText(Path.Combine(staging, "partial.txt"), "partial", Encoding.UTF8);

        var result = CliOutputPolicy.PrepareReviewDirectory(output, force: false, resume: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("ErrorOutputExistsRequiresForce", result.ErrorResourceKey);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "keep.txt")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void ReviewOutput_ResumeRefusesAmbiguousBackupsWithoutChangingThem()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("review");
        var first = workspace.PathOf($".review.{Guid.NewGuid():N}.backup");
        var second = workspace.PathOf($".review.{Guid.NewGuid():N}.backup");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        var result = CliOutputPolicy.PrepareReviewDirectory(output, force: false, resume: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("ErrorAmbiguousReviewRecovery", result.ErrorResourceKey);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void CleanupArtifacts_RemovesOnlyDestinationBoundTransactions()
    {
        using var workspace = new TemporaryWorkspace();
        var output = workspace.PathOf("gate.json");
        var recognized = workspace.PathOf($".gate.json.{Guid.NewGuid():N}.tmp");
        var unrelated = workspace.PathOf(".gate.json.manual.tmp");
        File.WriteAllText(recognized, "partial", Encoding.UTF8);
        File.WriteAllText(unrelated, "keep", Encoding.UTF8);

        var before = CliOutputPolicy.InspectArtifacts(output);
        var cleanup = CliOutputPolicy.CleanupArtifacts(output);

        Assert.Equal(1, before.TemporaryFiles);
        Assert.True(cleanup.IsSuccess);
        Assert.Equal(1, cleanup.RemovedArtifacts);
        Assert.False(File.Exists(recognized));
        Assert.True(File.Exists(unrelated));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-cli-output-policy-{Guid.NewGuid():N}");
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
