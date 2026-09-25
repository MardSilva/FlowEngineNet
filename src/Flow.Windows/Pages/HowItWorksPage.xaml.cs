using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Flow.Windows.Pages;

public sealed partial class HowItWorksPage : Page
{
    private readonly FrameworkElement[] _sections;
    private bool _synchronizingSelection;

    public HowItWorksPage(FlowWindowsTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);
        InitializeComponent();

        _sections =
        [
            OverviewSection,
            ResponsibilitiesSection,
            IdentitySection,
            PipelineSection,
            FormatsSection,
            SafetySection,
            ResearchSection,
            FaqSection,
        ];

        Localize(text);
        SelectSection(0);
        SectionScrollViewer.SizeChanged += (_, args) => UpdateSectionWidth(args.NewSize.Width);
        Loaded += (_, _) => UpdateSectionWidth(SectionScrollViewer.ActualWidth);
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_synchronizingSelection && SectionList.SelectedIndex >= 0)
        {
            SelectSection(SectionList.SelectedIndex);
        }
    }

    private void CompactSectionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_synchronizingSelection && CompactSectionPicker.SelectedIndex >= 0)
        {
            SelectSection(CompactSectionPicker.SelectedIndex);
        }
    }

    private void SelectSection(int index)
    {
        if (index < 0 || index >= _sections.Length)
        {
            return;
        }

        _synchronizingSelection = true;
        try
        {
            for (var position = 0; position < _sections.Length; position++)
            {
                _sections[position].Visibility = position == index
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            SectionList.SelectedIndex = index;
            CompactSectionPicker.SelectedIndex = index;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void UpdateSectionWidth(double availableWidth)
    {
        if (availableWidth > 0)
        {
            SectionContent.Width = Math.Min(SectionContent.MaxWidth, availableWidth);
        }
    }

    private void Localize(FlowWindowsTextCatalog text)
    {
        TitleText.Text = text["HowTitle"];
        LeadText.Text = text["HowLead"];
        OnThisPageText.Text = text["HowOnThisPage"];
        AutomationProperties.SetName(SectionList, text["HowOnThisPage"]);
        AutomationProperties.SetName(CompactSectionPicker, text["HowChooseSection"]);

        var sectionNames = new[]
        {
            text["HowOverviewNav"],
            text["HowResponsibilitiesNav"],
            text["HowIdentityNav"],
            text["HowPipelineNav"],
            text["HowFormatsNav"],
            text["HowSafetyNav"],
            text["HowResearchNav"],
            text["HowFaqNav"],
        };
        SectionList.ItemsSource = sectionNames;
        CompactSectionPicker.ItemsSource = sectionNames;

        OverviewTitle.Text = text["HowOverviewTitle"];
        OverviewText.Text = text["HowOverviewText"];
        InvariantText.Text = text["Invariant"];
        InvariantExplanation.Text = text["InvariantExplanation"];
        OverviewResultTitle.Text = text["HowOverviewResultTitle"];
        OverviewResultText.Text = text["HowOverviewResultText"];

        ResponsibilitiesTitle.Text = text["HowResponsibilitiesTitle"];
        ResponsibilitiesText.Text = text["HowResponsibilitiesText"];
        DocumentTitle.Text = text["DocumentTitle"];
        DocumentDescription.Text = text["DocumentDescription"];
        LayoutTitle.Text = text["LayoutTitle"];
        LayoutDescription.Text = text["LayoutDescription"];
        RenderingTitle.Text = text["RenderingTitle"];
        RenderingDescription.Text = text["RenderingDescription"];
        ResponsibilitiesNote.Text = text["HowResponsibilitiesNote"];

        IdentityTitle.Text = text["HowIdentityTitle"];
        IdentityText.Text = text["HowIdentityText"];
        HashChangesTitle.Text = text["HowHashChangesTitle"];
        HashChangesText.Text = text["HowHashChangesText"];
        HashStableTitle.Text = text["HowHashStableTitle"];
        HashStableText.Text = text["HowHashStableText"];
        IdentityNote.Text = text["HowIdentityNote"];

        PipelineTitle.Text = text["HowPipelineTitle"];
        PipelineText.Text = text["HowPipelineText"];
        PipelineItems.ItemsSource = new[]
        {
            new ExplanationItem(text["HowPipelineInspectTitle"], text["HowPipelineInspectText"]),
            new ExplanationItem(text["HowPipelineImportTitle"], text["HowPipelineImportText"]),
            new ExplanationItem(text["HowPipelineValidateTitle"], text["HowPipelineValidateText"]),
            new ExplanationItem(text["HowPipelineRenderTitle"], text["HowPipelineRenderText"]),
            new ExplanationItem(text["HowPipelineResultTitle"], text["HowPipelineResultText"]),
        };

        FormatsTitle.Text = text["HowFormatsTitle"];
        FormatsText.Text = text["HowFormatsText"];
        FormatItems.ItemsSource = new[]
        {
            new ExplanationItem(text["HowEpubTitle"], text["HowEpubText"]),
            new ExplanationItem(text["HowFlowTitle"], text["HowFlowText"]),
            new ExplanationItem(text["HowPdfTitle"], text["HowPdfText"]),
        };

        SafetyTitle.Text = text["HowSafetyTitle"];
        SafetyText.Text = text["HowSafetyText"];
        SafetyItems.ItemsSource = new[]
        {
            new ExplanationItem(text["HowSafetySourceTitle"], text["HowSafetySourceText"]),
            new ExplanationItem(text["HowSafetyDiagnosticsTitle"], text["HowSafetyDiagnosticsText"]),
            new ExplanationItem(text["HowSafetyPreviewTitle"], text["HowSafetyPreviewText"]),
        };

        ResearchTitle.Text = text["HowResearchTitle"];
        ResearchText.Text = text["HowResearchText"];
        AvailableTitle.Text = text["HowAvailableTitle"];
        AvailableText.Text = text["HowAvailableText"];
        FutureTitle.Text = text["HowFutureTitle"];
        FutureText.Text = text["HowFutureText"];
        ResearchNote.Text = text["HowResearchNote"];

        FaqTitle.Text = text["HowFaqTitle"];
        FaqText.Text = text["HowFaqText"];
        FaqEpub.Header = text["HowFaqEpubQuestion"];
        FaqEpubAnswer.Text = text["HowFaqEpubAnswer"];
        FaqAppearance.Header = text["HowFaqAppearanceQuestion"];
        FaqAppearanceAnswer.Text = text["HowFaqAppearanceAnswer"];
        FaqLoss.Header = text["HowFaqLossQuestion"];
        FaqLossAnswer.Text = text["HowFaqLossAnswer"];
        FaqReader.Header = text["HowFaqReaderQuestion"];
        FaqReaderAnswer.Text = text["HowFaqReaderAnswer"];
        FaqOffline.Header = text["HowFaqOfflineQuestion"];
        FaqOfflineAnswer.Text = text["HowFaqOfflineAnswer"];
    }

    private sealed record ExplanationItem(string Title, string Description);
}
