using Flow.Windows.Shell;
using Microsoft.UI.Xaml.Controls;

namespace Flow.Windows.Pages;

public sealed partial class HowItWorksPage : Page
{
    public HowItWorksPage(FlowWindowsTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);
        InitializeComponent();
        TitleText.Text = text["HowTitle"];
        LeadText.Text = text["HowLead"];
        InvariantText.Text = text["Invariant"];
        InvariantExplanation.Text = text["InvariantExplanation"];
        DocumentTitle.Text = text["DocumentTitle"];
        DocumentDescription.Text = text["DocumentDescription"];
        LayoutTitle.Text = text["LayoutTitle"];
        LayoutDescription.Text = text["LayoutDescription"];
        RenderingTitle.Text = text["RenderingTitle"];
        RenderingDescription.Text = text["RenderingDescription"];
        WhyTitle.Text = text["WhyTitle"];
        WhyText.Text = text["WhyText"];
    }
}
