using Avalonia.Controls.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PlaybackPresent.ViewModels
{
   public partial class SettingsProps : BaseViewModel
   {
      [JsonPropertyName("firstRun")]
      public bool FirstRun { get; set; } = true;
      [JsonIgnore]
      public bool RunOnStartup
      {
         get
         {
            try
            {
               var rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
               if (rk is null)
                  return false;
               var value = rk.GetValue("PlaybackPresent") as string;
               return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
               MessageBox.Show("Failed to load registry: " + ex.Message);
               return false;
            }
         }
         set
         {
            if (value == true)
            {
               try
               {
                  using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                  {
                     if (key == null)
                        throw new InvalidOperationException("Unable to open registry to set startup entry.");
                     if (Environment.ProcessPath is null)
                        throw new InvalidOperationException("Unable to determine application path for startup entry.");
                     key.SetValue("PlaybackPresent", $"\"{Environment.ProcessPath}\"");
                  }
               }
               catch (Exception ex)
               {
                  MessageBox.Show("Failed to add startup entry to registry: " + ex.Message);
                  value = false;
                  return;
               }
            }
            else
            {
               try
               {
                  using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                  {
                     key?.DeleteValue("PlaybackPresent", throwOnMissingValue: false);
                  }
               }
               catch
               {
                  MessageBox.Show("Failed to remove startup entry from registry.");
               }
            }
         }
      }
      public void OnPropertyChangedExternal(string? propertyName)
      {
         OnPropertyChanged(propertyName);
      }
      [JsonPropertyName("gradientMode")]
      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(SpectrumBrush))]
      private GradientType gradientMode = GradientType.Linear;
      [JsonIgnore]
      public GradientType[] AllGradientModes { get; } = (GradientType[])Enum.GetValues(typeof(GradientType));

      [JsonIgnore]
      public int[] FFTSizeChoices { get; } = Enumerable.Range(4, 10).Select(x => 1 << x).ToArray();

      [ObservableProperty]
      [JsonIgnore]
      private int screenWidth = 1280;
      [ObservableProperty]
      [JsonIgnore]
      private int screenHeight = 720;
      public SettingsProps()
      {
         SpectrumGradientStops = new();
         SpectrumGradientStops.CollectionChanged += (s, e) =>
         {
            OnPropertyChanged(nameof(SpectrumBrush));
         };
      }
      [JsonIgnore]
      [ObservableProperty]
      private bool isSettingsWindowOpen = false;
      [ObservableProperty]
      [JsonPropertyName("windowWidth")]
      private double windowWidth = 400;
      [ObservableProperty]
      [JsonPropertyName("windowHeight")]
      private double windowHeight = 100;
      [ObservableProperty]
      [JsonIgnore]
      private double sliderMaxValue = 500; //at runtime will be the same as Screenwidth

      [ObservableProperty]
      [JsonIgnore]
      private int selectedGradientStopIndex = -1;

      [ObservableProperty]
      [JsonPropertyName("windowPosX")]
      private int windowPosX = 100;
      [ObservableProperty]
      [JsonPropertyName("windowPosY")]
      private int windowPosY = 100;

      [ObservableProperty]
      [JsonPropertyName("visibilityTimeout")]
      private int visibilityTimeout = 2000;

      [ObservableProperty]
      [JsonPropertyName("audioSpectrumEnabled")]
      private bool audioSpectrumEnabled = true;

      [ObservableProperty]
      [JsonPropertyName("barCount")]
      private int barCount = 256;
      [ObservableProperty]
      [JsonPropertyName("fftSize")]
      private int fftSize = 1024;

      [JsonPropertyName("spectrumGradientStops")]
      public List<SpectrumGradientStopListItem> GradientStopsAsList
      {
         get
         {
            return SpectrumGradientStops.ToList();
         }
         set
         {
            SpectrumGradientStops.Clear();
            if (value is null)
            {
               OnPropertyChanged(nameof(SpectrumBrush));
               return;
            }
            foreach (var stop in value)
            {
               SpectrumGradientStops.Add(stop);
            }
            OnPropertyChanged(nameof(SpectrumBrush));
         }
      }

      [JsonIgnore]
      public ObservableCollection<SpectrumGradientStopListItem> SpectrumGradientStops { get; }

      //new ()
      //{
      //   new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(255,0,255),0.0)),
      //   new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(255,255,0),0.5)),
      //   new SpectrumGradientStopListItem( new GradientStop(Color.FromRgb(0,255,255),1.0)),
      //};


      [JsonIgnore]
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

   [JsonConverter(typeof(SpectrumGradientStopListItemJsonConverter))]
   public partial class SpectrumGradientStopListItem : ObservableObject
   {
      public SpectrumGradientStopListItem()
      {
         // Parameterless ctor required by System.Text.Json if converter is not used;
         // kept for safety so other consumers can instantiate easily.
         gradientStopValue = new GradientStop(Colors.White, 0.0);
      }

      public SpectrumGradientStopListItem(GradientStop Gradient)
      {
         gradientStopValue = Gradient;
      }

      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(Offset))]
      [NotifyPropertyChangedFor(nameof(ColorHex))]
      [NotifyPropertyChangedFor(nameof(ColorAsBrush))]
      private GradientStop gradientStopValue;

      [JsonIgnore]
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

      [JsonPropertyName("colorHex")]
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
      [JsonPropertyName("offset")]
      public double Offset
      {
         get => GradientStopValue.Offset;
         set
         {
            GradientStopValue.Offset = value;
         }
      }
   }

   // Custom JsonConverter for SpectrumGradientStopListItem:
   // - Ensures only the colorHex and offset properties are written/read.
   // - Avoids attempting to serialize Avalonia types (GradientStop / Color) directly,
   //   which can fail with System.Text.Json.
   internal sealed class SpectrumGradientStopListItemJsonConverter : JsonConverter<SpectrumGradientStopListItem>
   {
      public override SpectrumGradientStopListItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
      {
         if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

         string? colorHex = null;
         double offset = 0.0;

         while (reader.Read())
         {
            if (reader.TokenType == JsonTokenType.EndObject)
               break;

            if (reader.TokenType != JsonTokenType.PropertyName)
               continue;

            string propName = reader.GetString()!;
            reader.Read();

            switch (propName)
            {
               case "colorHex":
                  colorHex = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                  break;
               case "offset":
                  offset = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0.0;
                  break;
               default:
                  reader.Skip();
                  break;
            }
         }

         Color color = Colors.White;
         if (!string.IsNullOrEmpty(colorHex))
         {
            var parsed = ColorToHexConverter.ParseHexString(colorHex, Avalonia.Controls.AlphaComponentPosition.Trailing);
            if (parsed is not null)
               color = parsed.Value;
         }

         return new SpectrumGradientStopListItem(new GradientStop(color, offset));
      }

      public override void Write(Utf8JsonWriter writer, SpectrumGradientStopListItem value, JsonSerializerOptions options)
      {
         writer.WriteStartObject();
         writer.WriteString("colorHex", value.ColorHex);
         writer.WriteNumber("offset", value.Offset);
         writer.WriteEndObject();
      }
   }

}
