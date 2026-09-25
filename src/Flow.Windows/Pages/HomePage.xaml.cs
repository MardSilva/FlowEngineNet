using Flow.Windows.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Flow.Windows.Pages;

public sealed partial class HomePage : Page
{
    private readonly Action<FlowWindowsOperationKind> _openOperation;

    public HomePage(FlowWindowsTextCatalog text, Action<FlowWindowsOperationKind> openOperation)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(openOperation);
        _openOperation = openOperation;
        InitializeComponent();
        TitleText.Text = text["HomeTitle"];
        LeadText.Text = text["HomeLead"];
        ActionsTitle.Text = text["CommonActions"];
        InspectTitle.Text = text["InspectTitle"];
        InspectDescription.Text = text["InspectDescription"];
        ImportTitle.Text = text["ImportTitle"];
        ImportDescription.Text = text["ImportDescription"];
        ValidateTitle.Text = text["ValidateTitle"];
        ValidateDescription.Text = text["ValidateDescription"];
        ConfigureUnavailableAction(
            InspectButton,
            text["InspectTitle"],
            text["ActionButton"],
            text["ActionUnavailable"]);
        ConfigureUnavailableAction(
            ImportButton,
            text["ImportTitle"],
            text["ActionButton"],
            text["ActionUnavailable"]);
        ConfigureUnavailableAction(
            ValidateButton,
            text["ValidateTitle"],
            text["ActionButton"],
            text["ActionUnavailable"]);
        PageScrollViewer.SizeChanged += (_, args) => UpdateContentWidth(args.NewSize.Width);
        Loaded += (_, _) => UpdateContentWidth(PageScrollViewer.ActualWidth);
    }

    private void InspectButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _openOperation(FlowWindowsOperationKind.Inspect);

    private void ImportButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _openOperation(FlowWindowsOperationKind.Import);

    private void ValidateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _openOperation(FlowWindowsOperationKind.Validate);

    private void UpdateContentWidth(double availableWidth)
    {
        if (availableWidth > 0)
        {
            PageContent.Width = Math.Min(PageContent.MaxWidth, availableWidth);
        }
    }

    private static void ConfigureUnavailableAction(
        Button button,
        string action,
        string buttonText,
        string explanation)
    {
        button.Content = buttonText;
        AutomationProperties.SetName(button, action);
        AutomationProperties.SetHelpText(button, explanation);
    }
}
