using System.Text;
using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubSpineProcessingTests
{
    [Fact]
    public async Task ImportAsync_LongSpineUsesOnlyDeclaredOrderAndRetainsSupplementalItems()
    {
        const int chapterCount = 18;
        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        var resources = new Dictionary<string, string>();
        for (var index = 0; index < chapterCount; index++)
        {
            manifest.Append($"<item id=\"item-{index}\" href=\"long/chapter-{index}.xhtml\" media-type=\"application/xhtml+xml\" properties=\"scripted remote-resources\"/>");
            resources[$"EPUB/long/chapter-{index}.xhtml"] = Xhtml($"Chapter {index}");
        }

        for (var index = chapterCount - 1; index >= 0; index--)
        {
            var linear = index == 7 ? " linear=\"no\"" : index == 6 ? " linear=\"yes\"" : string.Empty;
            spine.Append($"<itemref idref=\"item-{index}\"{linear}/>");
        }

        var package = Package(manifest.ToString(), spine.ToString());
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Xhtml("Unused default"),
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: resources);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var chapters = Assert.IsType<FlowDocument>(result.Document).Content.Children.Cast<Chapter>().ToArray();
        Assert.Equal(chapterCount, chapters.Length);
        Assert.Equal(
            Enumerable.Range(0, chapterCount).Reverse().Select(static index => $"Chapter {index}"),
            chapters.Select(HeadingText));

        var report = Assert.IsType<EpubPackageProcessingReport>(result.ProcessingReport);
        Assert.Equal(Enumerable.Range(0, chapterCount), report.Spine.Select(static item => item.Position));
        Assert.All(report.Spine, static item => Assert.Equal(EpubSpineDisposition.Included, item.Disposition));
        Assert.Equal(EpubSpineReadingRole.Supplemental, report.Spine.Single(static item => item.RequestedItemId == "item-7").ReadingRole);
        Assert.Equal(EpubSpineReadingRole.Linear, report.Spine.Single(static item => item.RequestedItemId == "item-6").ReadingRole);
        Assert.Equal(["remote-resources", "scripted"], report.Manifest[0].Properties.ToArray());
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.NonLinearSpineItem);
    }

    [Fact]
    public async Task ImportAsync_RepeatedSpineReferenceKeepsEveryOccurrenceWithUniqueStableIds()
    {
        var package = Package(
            """
            <item id="one" href="repeat/one.xhtml" media-type="application/xhtml+xml"/>
            <item id="two" href="repeat/two.xhtml" media-type="application/xhtml+xml"/>
            """,
            """
            <itemref idref="one"/><itemref idref="two"/><itemref idref="one"/>
            """);
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Xhtml("Unused default"),
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/repeat/one.xhtml"] = Xhtml("One"),
                ["EPUB/repeat/two.xhtml"] = Xhtml("Two"),
            });

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        var chapters = document.Content.Children.Cast<Chapter>().ToArray();
        Assert.Equal(["One", "Two", "One"], chapters.Select(HeadingText));
        Assert.Equal(3, chapters.Select(static chapter => chapter.Id).Distinct().Count());
        var decisions = Assert.IsType<EpubPackageProcessingReport>(result.ProcessingReport).Spine;
        Assert.False(decisions[0].IsRepeatedReference);
        Assert.True(decisions[2].IsRepeatedReference);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.RepeatedSpineItem);
    }

    [Fact]
    public async Task ImportAsync_UsesMultiStepXhtmlFallbackAndDiagnosesMediaOverlay()
    {
        var package = Package(
            """
            <item id="source" href="fallback/source.bin" media-type="application/vnd.example" fallback="middle" properties="rendition:layout-pre-paginated" media-overlay="overlay"/>
            <item id="middle" href="fallback/middle.svg" media-type="image/svg+xml" fallback="target"/>
            <item id="target" href="fallback/target.xhtml" media-type="application/xhtml+xml"/>
            <item id="overlay" href="fallback/overlay.smil" media-type="application/smil+xml"/>
            """,
            "<itemref idref=\"source\"/>");
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Xhtml("Unused default"),
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/fallback/source.bin"] = "source",
                ["EPUB/fallback/middle.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>",
                ["EPUB/fallback/target.xhtml"] = Xhtml("Fallback target"),
                ["EPUB/fallback/overlay.smil"] = "<smil xmlns=\"http://www.w3.org/ns/SMIL\"/>",
            });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Equal("Fallback target", HeadingText(Assert.Single(Assert.IsType<FlowDocument>(result.Document).Content.Children.Cast<Chapter>())));
        var report = Assert.IsType<EpubPackageProcessingReport>(result.ProcessingReport);
        var decision = Assert.Single(report.Spine);
        Assert.Equal(EpubSpineDisposition.Substituted, decision.Disposition);
        Assert.Equal(EpubSpineDecisionReason.XhtmlFallback, decision.Reason);
        Assert.Equal("target", decision.SelectedItemId);
        Assert.Equal(["source", "middle", "target"], decision.FallbackChain.ToArray());
        Assert.Equal("middle", report.Manifest.Single(static item => item.Id == "source").FallbackId);
        Assert.Equal("overlay", report.Manifest.Single(static item => item.Id == "source").MediaOverlayId);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsupportedMediaOverlay);

        epub.Position = 0;
        var inspection = await new EpubPublicationInspector().InspectAsync(epub);
        var inspectedSource = inspection.Manifest.Single(static item => item.Id == "source");
        Assert.Equal("middle", inspectedSource.FallbackId);
        Assert.Equal("overlay", inspectedSource.MediaOverlayId);
    }

    [Fact]
    public async Task ImportAsync_ExcludesBrokenCircularMissingAndUnsupportedSpineItemsWithTypedReasons()
    {
        var package = Package(
            """
            <item id="good" href="problems/good.xhtml" media-type="application/xhtml+xml"/>
            <item id="cycle-a" href="problems/a.bin" media-type="application/a" fallback="cycle-b"/>
            <item id="cycle-b" href="problems/b.bin" media-type="application/b" fallback="cycle-a"/>
            <item id="broken" href="problems/broken.bin" media-type="application/broken" fallback="absent-fallback"/>
            <item id="missing-file" href="problems/missing.xhtml" media-type="application/xhtml+xml"/>
            <item id="unsupported" href="problems/audio.mp3" media-type="audio/mpeg"/>
            """,
            """
            <itemref idref="good"/><itemref idref="cycle-a"/><itemref idref="broken"/>
            <itemref idref="missing-file"/><itemref idref="unsupported" linear="no"/><itemref idref="missing-id"/>
            """);
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Xhtml("Unused default"),
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/problems/good.xhtml"] = Xhtml("Good"),
                ["EPUB/problems/a.bin"] = "a",
                ["EPUB/problems/b.bin"] = "b",
                ["EPUB/problems/broken.bin"] = "broken",
                ["EPUB/problems/audio.mp3"] = "audio",
            });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Equal("Good", HeadingText(Assert.Single(Assert.IsType<FlowDocument>(result.Document).Content.Children.Cast<Chapter>())));
        var decisions = Assert.IsType<EpubPackageProcessingReport>(result.ProcessingReport).Spine;
        Assert.Equal(6, decisions.Length);
        Assert.Equal(EpubSpineDisposition.Included, decisions[0].Disposition);
        Assert.Equal(EpubSpineDecisionReason.CircularFallback, decisions[1].Reason);
        Assert.Equal(EpubSpineDecisionReason.BrokenFallback, decisions[2].Reason);
        Assert.Equal(EpubSpineDecisionReason.MissingArchiveResource, decisions[3].Reason);
        Assert.Equal(EpubSpineDecisionReason.UnsupportedMediaType, decisions[4].Reason);
        Assert.Equal(EpubSpineReadingRole.Supplemental, decisions[4].ReadingRole);
        Assert.Equal(EpubSpineDecisionReason.MissingManifestItem, decisions[5].Reason);
        Assert.All(decisions.Skip(1), static decision => Assert.Equal(EpubSpineDisposition.Excluded, decision.Disposition));
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.CircularFallback);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.BrokenFallback);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MissingResource);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsupportedResource);
    }

    [Fact]
    public void PackageProcessingReport_CopiesCollectionsAndOrdersSpineByPosition()
    {
        var manifest = new List<EpubManifestResourceDecision>
        {
            new("one", "one.xhtml", "application/xhtml+xml", ["scripted"], null, null, true),
        };
        var spine = new List<EpubSpineProcessingDecision>
        {
            new(1, "one", EpubSpineReadingRole.Linear, true, EpubSpineDisposition.Included, EpubSpineDecisionReason.DirectXhtml, "one", "one.xhtml", ["one"]),
            new(0, "one", EpubSpineReadingRole.Linear, false, EpubSpineDisposition.Included, EpubSpineDecisionReason.DirectXhtml, "one", "one.xhtml", ["one"]),
        };

        var report = new EpubPackageProcessingReport(manifest, spine);
        manifest.Clear();
        spine.Clear();

        Assert.Single(report.Manifest);
        Assert.Equal([0, 1], report.Spine.Select(static item => item.Position));
    }

    private static string Package(string manifest, string spine) => $$"""
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:spine-processing</dc:identifier>
            <dc:title>Spine processing</dc:title>
          </metadata>
          <manifest>{{manifest}}</manifest>
          <spine>{{spine}}</spine>
        </package>
        """;

    private static string Xhtml(string title) => $$"""
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>{{title}}</title></head>
          <body><h1 id="heading">{{title}}</h1><p>{{title}} content.</p></body>
        </html>
        """;

    private static string HeadingText(Chapter chapter) =>
        Assert.IsType<Text>(Assert.IsType<Heading>(chapter.Children[0]).Content[0]).Value;
}
