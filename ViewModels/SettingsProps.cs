using Avalonia.Controls.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlaybackPresent.ViewModels
{
   public partial class SettingsProps : BaseViewModel
   {
      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(SpectrumBrush))]
      private GradientType gradientMode = GradientType.Linear;
      public GradientType[] AllGradientModes { get; } = (GradientType[])Enum.GetValues(typeof(GradientType));

      public int[] FFTSizeChoices { get; } = Enumerable.Range(4, 10).Select(x => 1 << x).ToArray();

      [ObservableProperty]
      private int screenWidth = 1280;
      [ObservableProperty]
      private int screenHeight = 720;
      public SettingsProps()
      {
         SpectrumGradientStops.CollectionChanged += (s, e) =>
         {
            OnPropertyChanged(nameof(SpectrumBrush));
         };
      }
      [ObservableProperty]
      private bool isSettingsWindowOpen = false;
      [ObservableProperty]
      private double windowWidth = 400;
      [ObservableProperty]
      private double windowHeight = 100;
      [ObservableProperty]
      private double sliderMaxValue = 500; //at runtime will be the same as Screenwidth

      [ObservableProperty]
      private int selectedGradientStopIndex = -1;

      [ObservableProperty]
      private int windowPosX = 100;
      [ObservableProperty]
      private int windowPosY = 100;

      [ObservableProperty]
      private int visibilityTimeout = 3000;

      [ObservableProperty]
      private bool audioSpectrumEnabled = true;

      [ObservableProperty]
      private int barCount = 256;
      [ObservableProperty]
      private int fftSize = 1024;
      public ObservableCollection<SpectrumGradientStopListItem> SpectrumGradientStops { get; set; } = new()
      {
         new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(255,0,255),0.0)),
         new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(255,255,0),0.5)),
         new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(0,255,255),1.0)),
      };

      public GradientBrush SpectrumBrush => GetSpectrumGradientBrush();
      public GradientBrush GetSpectrumGradientBrush()
      {
         GradientBrush gradient;

         switch (GradientMode)
         {
            case GradientType.Linear:
               gradient = new LinearGradientBrush();
               break;
            case GradientType.Radial:
               gradient = new RadialGradientBrush();

               break;
            case GradientType.Conical:
               gradient = new ConicGradientBrush();
               break;
            default:
               gradient = new LinearGradientBrush();
               break;
         }
         foreach (var stop in SpectrumGradientStops)
         {
            gradient.GradientStops.Add(stop.GradientStopValue);
         }
         return gradient;
      }

      [RelayCommand]
      private void AddSpectrumGradientStop()
      {
         SpectrumGradientStops.Add(new SpectrumGradientStopListItem(new GradientStop(Colors.White, 0.5)));
      }
      [RelayCommand]
      private void DeleteSelectedGradientStop()
      {
         if (SelectedGradientStopIndex == -1)
            return;
         SpectrumGradientStops.RemoveAt(SelectedGradientStopIndex);
      }
   }
   public partial class SpectrumGradientStopListItem : ObservableObject
   {
      public SpectrumGradientStopListItem(GradientStop Gradient)
      {
         gradientStopValue = Gradient;
      }
      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(Offset))]
      [NotifyPropertyChangedFor(nameof(ColorHex))]
      [NotifyPropertyChangedFor(nameof(ColorAsBrush))]
      private GradientStop gradientStopValue;
      public Brush ColorAsBrush => new SolidColorBrush(GradientStopValue.Color);


      public Color GradientStopColor
      {
         get => GradientStopValue.Color;
         set
         {
            GradientStopValue.Color = value;
            OnPropertyChanged(nameof(GradientStopColor));
            OnPropertyChanged(nameof(ColorHex));
            OnPropertyChanged(nameof(ColorAsBrush));
         }
      }

      public string ColorHex
      {
         get
         {
            return ColorToHexConverter.ToHexString(GradientStopValue.Color, Avalonia.Controls.AlphaComponentPosition.Trailing);
         }
         set
         {
            var color = ColorToHexConverter.ParseHexString(value, Avalonia.Controls.AlphaComponentPosition.Trailing);
            if (color is null)
            {

            }
            else
            {
               GradientStopValue.Color = color.Value;
               OnPropertyChanged(nameof(ColorHex));
               OnPropertyChanged(nameof(ColorAsBrush));
            }
         }
      }
      public double Offset
      {
         get => GradientStopValue.Offset;
         set
         {
            GradientStopValue.Offset = value;
         }
      }
   }

}
