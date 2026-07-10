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

        FlowDirection = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? (Avalonia.Media.FlowDirection)1
            : (Avalonia.Media.FlowDirection)0;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
