using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PlaybackPresent.ViewModels;

namespace PlaybackPresent;

public partial class PlaybackPresenterSettings : Window
{
   public PlaybackPresenterSettings(SettingsProps vm)
   {
      this.DataContext = vm;
      InitializeComponent();
   }
   public PlaybackPresenterSettings()
   {
      this.DataContext= new SettingsProps(); //Dummy for design time;
      InitializeComponent();
   }
}