using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
