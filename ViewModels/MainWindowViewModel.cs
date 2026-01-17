using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Media.Playback;
namespace PlaybackPresent.ViewModels;

public partial class MainWindowViewModel : BaseViewModel
{
   public SettingsProps SettingsProperties { get; set; } = new();

   public MainWindowViewModel()
   {
   }

   public async Task InitializeAsync()
   {

   }

}
