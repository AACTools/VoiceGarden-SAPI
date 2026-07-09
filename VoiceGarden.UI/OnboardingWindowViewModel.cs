using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VoiceGarden.UI;

public partial class OnboardingWindowViewModel : ObservableObject
{
    private static readonly IBrush ActiveDot = new SolidColorBrush(Color.Parse("#F97316"));
    private static readonly IBrush InactiveDot = new SolidColorBrush(Color.Parse("#CBD5E1"));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPage1))]
    [NotifyPropertyChangedFor(nameof(IsPage2))]
    [NotifyPropertyChangedFor(nameof(IsPage3))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(Dot1Brush))]
    [NotifyPropertyChangedFor(nameof(Dot2Brush))]
    [NotifyPropertyChangedFor(nameof(Dot3Brush))]
    private int _currentPage = 1;

    public bool IsPage1 => CurrentPage == 1;
    public bool IsPage2 => CurrentPage == 2;
    public bool IsPage3 => CurrentPage == 3;

    public bool CanGoBack => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < 3;

    public IBrush Dot1Brush => CurrentPage >= 1 ? ActiveDot : InactiveDot;
    public IBrush Dot2Brush => CurrentPage >= 2 ? ActiveDot : InactiveDot;
    public IBrush Dot3Brush => CurrentPage >= 3 ? ActiveDot : InactiveDot;

    public bool AnalyticsOptIn { get; private set; }

    [RelayCommand]
    private void Next()
    {
        if (CurrentPage < 3)
            CurrentPage++;
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentPage > 1)
            CurrentPage--;
    }

    [RelayCommand]
    private void Accept()
    {
        AnalyticsOptIn = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Decline()
    {
        AnalyticsOptIn = false;
        RequestClose?.Invoke();
    }

    public event Action? RequestClose;
}
