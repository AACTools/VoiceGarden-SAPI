using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace VoiceGarden.UI;

public partial class OnboardingWindow : Window
{
    private readonly OnboardingWindowViewModel _vm;

    public bool AnalyticsOptIn => _vm.AnalyticsOptIn;

    public OnboardingWindow()
    {
        InitializeComponent();
        _vm = new OnboardingWindowViewModel();
        DataContext = _vm;
        _vm.RequestClose += () => Close();
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(OnboardingWindowViewModel.CurrentPage))
                AnnouncePageChange();
        };

        FlowDirection = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? (Avalonia.Media.FlowDirection)1
            : (Avalonia.Media.FlowDirection)0;

        AnnouncePageChange();
    }

    private void AnnouncePageChange()
    {
        var titles = new[] { "Welcome to VoiceGarden", "Getting Started", "Help Improve VoiceGarden" };
        var idx = _vm.CurrentPage - 1;
        var title = idx >= 0 && idx < titles.Length ? titles[idx] : "";
        // Update a live region to announce the page change
        var announcer = this.FindControl<TextBlock>("PageAnnouncer");
        if (announcer != null)
            announcer.Text = $"Page {_vm.CurrentPage} of 3: {title}";
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
