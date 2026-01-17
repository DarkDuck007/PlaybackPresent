using CommunityToolkit.Mvvm.ComponentModel;

namespace PlaybackPresent.ViewModels;

public partial class PlaybackItemViewModel:BaseViewModel
{
    [ObservableProperty]
    string title;
    [ObservableProperty]
    string description;

    [ObservableProperty] private double percentComplete;
    [ObservableProperty] private PlaybackStatus currentPlaybackStatus;
}
public enum  PlaybackStatus
{
    Idle,
    Playing,
    Paused,
}