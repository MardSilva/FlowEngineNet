namespace Flow.Epub.Tests;

public sealed class EpubOptionalLoadTests
{
    [Fact]
    [Trait("Category", "Load")]
    public async Task ImportExternalPublication_WhenExplicitlyConfigured()
    {
        var path = Environment.GetEnvironmentVariable("FLOW_EPUB_LOAD_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await using var source = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var result = await new EpubImporter().ImportAsync(source);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var metrics = Assert.IsType<EpubImportMetrics>(result.Metrics);
        Assert.True(metrics.ArchiveEntryCount > 0);
        Assert.True(metrics.NodesProduced > 0);
        Assert.True(metrics.CharactersProduced > 0);
    }
}
