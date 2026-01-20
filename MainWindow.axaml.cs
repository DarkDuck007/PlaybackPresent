using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NAudio.CoreAudioApi;
using PlaybackPresent.ViewModels;
using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.PointOfService;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Control = Avalonia.Controls.Control;

namespace PlaybackPresent
{
   public partial class MainWindow : Window
   {
      MainWindowViewModel VM;
      private static MMDeviceEnumerator enumer = new MMDeviceEnumerator();
      private MMDevice dev = enumer.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
      private double volFullHeight = 0;
      PlaybackPresenterSettings SettingsWindow;
      LoopbackFftAnalyzer analyzer;
      System.Threading.Timer HideTimer;
      Animation FadeInAnimation;

      void RescheduleTimer()
      {
         HideTimer.Change(VM.SettingsProperties.VisibilityTimeout, Timeout.Infinite);
      }
      void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
      {
         CurrentValue = data.MasterVolume;
         FadeWindowIn();
      }
      public async Task FadeWindowInAsync(bool force = false)
      {
         VolBar.Height = CurrentValue * volFullHeight;

         if (this.IsVisible && !force)
         {
            RescheduleTimer();
            return;
         }

         Spectrum.PauseRendering = false;
         Spectrum.Data = Array.Empty<float>();
         Spectrum.InvalidateVisual();
         this.Show();
         await FadeInAnimation.RunAsync(this);
         RescheduleTimer();
      }
      public void FadeWindowIn()
      {
         Dispatcher.UIThread.Post(async () =>
         {
            await FadeWindowInAsync();
         });
      }
      protected override void OnUnloaded(RoutedEventArgs e)
      {
         //base.OnUnloaded(e);
         //analyzer.Stop();
      }

      // Use a dedicated async loader so you can attach handlers to the new instance


      protected override async void OnLoaded(RoutedEventArgs e)
      {
         base.OnLoaded(e);

         // Fire-and-forget loader (do not block UI). If you prefer to await, make this async.
         Dispatcher.UIThread.Post(async () =>
          {
             try
             {
                if (File.Exists("config.json"))
                {
                   using var fs = File.OpenRead("config.json");
                   var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                   {
                      PropertyNameCaseInsensitive = true
                   };
                   options.Converters.Add(new JsonStringEnumConverter()); // allow enum strings
                                                                          // SpectrumGradientStopListItem has its own [JsonConverter], so no extra converter required for it.

                   var newProps = JsonSerializer.Deserialize<SettingsProps>(fs, options);
                   if (newProps is not null)
                   {
                      VM.SettingsProperties.WindowPosX = newProps.WindowPosX;
                      VM.SettingsProperties.WindowPosY = newProps.WindowPosY;
                      VM.SettingsProperties.AudioSpectrumEnabled = newProps.AudioSpectrumEnabled;
                      VM.SettingsProperties.FftSize = newProps.FftSize;
                      VM.SettingsProperties.BarCount = newProps.BarCount;
                      VM.SettingsProperties.VisibilityTimeout = newProps.VisibilityTimeout;
                      VM.SettingsProperties.SpectrumGradientStops.Clear();
                      VM.SettingsProperties.WindowWidth= newProps.WindowWidth;
                      VM.SettingsProperties.WindowHeight= newProps.WindowHeight;
                      foreach (var stop in newProps.SpectrumGradientStops)
                      {
                         VM.SettingsProperties.SpectrumGradientStops.Add(stop);
                      }
                      //// detach old handler, replace, reattach
                      //var old = VM.SettingsProperties;
                      //if (old is not null)
                      //   old.PropertyChanged -= SettingsProperties_PropertyChanged;

                      //VM.SettingsProperties = newProps;

                      //// Reattach handler so MainWindow reacts to changes on the new object
                      //VM.SettingsProperties.PropertyChanged += SettingsProperties_PropertyChanged;
                   }
                }
             }
             catch (Exception ex)
             {
                Debug.WriteLine("Failed to load config.json: " + ex.Message);
                // keep defaults (VM.SettingsProperties already set in ViewModel)
             }
             finally
             {
                // Ensure DataContext and UI are refreshed
                this.DataContext = VM;
                VM.SettingsProperties.OnPropertyChangedExternal(null);
             }
          });
      }

      protected override void OnOpened(EventArgs e)
      {
         base.OnOpened(e);
         // Example: Set the window position to (0,0) (top-left corner)
         Position = new PixelPoint(VM.SettingsProperties.WindowPosX, VM.SettingsProperties.WindowPosY);
         CurrentValue = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
         VolBar.Height = CurrentValue * volFullHeight;
         // If you need to position it relative to the screen size (e.g., bottom right)
         // you would calculate the desired position using the screens working area.
         //var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
         //if (screen != null)
         //{
         //   var workingArea = screen.WorkingArea;
         //   // Example: Position near the bottom-right (adjust calculation as needed)
         //   var newX = workingArea.Width - Bounds.Width - 10;
         //   var newY = workingArea.Height - Bounds.Height - 10;
         //   Position = new PixelPoint((int)newX, (int)newY);
         //}
      }

      private void ShowWindow()
      {

      }
      private void HideWindow()
      {
         Dispatcher.UIThread.Post(async () =>
         {
            var animation = (Animation)this.Resources["FadeOut"];
            await animation.RunAsync(this);
            Spectrum.PauseRendering = true;
            //analyzer.Stop();
            Spectrum.Data = Array.Empty<float>();
            Spectrum.InvalidateVisual();
            DataRate.Text = analyzer.DataRate.ToString();
            RequestAnimationFrame(new Action<TimeSpan>((time) =>
            {
               Spectrum.InvalidateVisual();
               RequestAnimationFrame(new Action<TimeSpan>((time) =>
               {
                  Spectrum.InvalidateVisual();
                  this.Hide();
               }));
            }));
         });
      }
      public MainWindow(MainWindowViewModel? ViewModel = null)
      {
         InitializeComponent();
         if (ViewModel is null)
            ViewModel = new MainWindowViewModel();
         this.VM = ViewModel;
         HideTimer = new(async _ =>
         {
            if (VM.SettingsProperties.IsSettingsWindowOpen || this.IsPointerOver)
            {
               RescheduleTimer();
               return;
            }

            if (analyzer is not null)
            {
               //double targetOpacity = 1.0;
               //while (targetOpacity > 0.01)
               //{
               //   Dispatcher.UIThread.Post(() =>
               //   {
               //      targetOpacity -= 0.1;
               //      this.Opacity = targetOpacity;
               //   });
               //   Thread.Sleep(16); // Approximate 60 FPS
               //}

               HideWindow();
            }

         }, null, Timeout.Infinite, Timeout.Infinite);
         this.DataContext = VM;
         VM.SettingsProperties.SliderMaxValue = this.Screens.Primary?.Bounds.Width ?? 1024;
         VM.SettingsProperties.ScreenWidth = this.Screens.Primary?.Bounds.Width ?? default;
         VM.SettingsProperties.ScreenHeight = this.Screens.Primary?.Bounds.Height ?? default;
         VM.PropertyChanged += VM_PropertyChanged;
         VM.SettingsProperties.PropertyChanged += SettingsProperties_PropertyChanged;
         FadeInAnimation = (Animation)this.Resources["FadeIn"];
         //Task.Run(async () =>
         //{
         //   var MediaSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
         //   GlobalSystemMediaTransportControlsSession session = MediaSessionManager.GetCurrentSession();
         //   if (session != null)
         //   {
         //      Gsmtcsm_CurrentSessionChanged(MediaSessionManager, null);
         //   }
         //   MediaSessionManager.CurrentSessionChanged += Gsmtcsm_CurrentSessionChanged;
         //})
      }

      private void SettingsProperties_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
      {
         if (e.PropertyName == null)
         {
            Position = new PixelPoint(VM.SettingsProperties.WindowPosX, VM.SettingsProperties.WindowPosY);
            if (VM.SettingsProperties.AudioSpectrumEnabled)
            {
               Spectrum.PauseRendering = false;
               analyzer.Start();

            }
            else
            {
               Spectrum.PauseRendering = true;
               Spectrum.Data = Array.Empty<float>();
               analyzer.Stop();
               Spectrum.InvalidateVisual();
            }
            analyzer.SetNewFftAggregator(VM.SettingsProperties.FftSize, VM.SettingsProperties.BarCount);
            return;
         }
         if (e.PropertyName.Equals(nameof(SettingsProps.WindowPosX)) && VM.SettingsProperties.WindowPosX != Position.X)
         {
            Position = new PixelPoint(VM.SettingsProperties.WindowPosX, Position.Y);
         }
         else if (e.PropertyName.Equals(nameof(SettingsProps.WindowPosY)) && VM.SettingsProperties.WindowPosY != Position.Y)
         {
            Position = new PixelPoint(Position.X, VM.SettingsProperties.WindowPosY);
         }
         else if (e.PropertyName.Equals(nameof(SettingsProps.AudioSpectrumEnabled)))
         {
            if (VM.SettingsProperties.AudioSpectrumEnabled)
            {
               Spectrum.PauseRendering = false;
               analyzer.Start();

            }
            else
            {
               Spectrum.PauseRendering = true;
               Spectrum.Data = Array.Empty<float>();
               analyzer.Stop();
               Spectrum.InvalidateVisual();
            }
         }
         else if (e.PropertyName.Equals(nameof(SettingsProps.FftSize)) || e.PropertyName.Equals(nameof(SettingsProps.BarCount)))
         {
            analyzer.SetNewFftAggregator(VM.SettingsProperties.FftSize, VM.SettingsProperties.BarCount);
         }
      }

      private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
      {

      }

      private void UpdatePlaybackInfo()
      {
      }

      GlobalSystemMediaTransportControlsSessionManager? MediaSessionManager;
      GlobalSystemMediaTransportControlsSession? CurrentSession;
      GlobalSystemMediaTransportControlsSessionMediaProperties? CurrentMediaProperties;
      GlobalSystemMediaTransportControlsSessionPlaybackInfo? CurrentPlaybackInfo;

      private async Task InitializeAsync()
      {
         MediaSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
         GlobalSystemMediaTransportControlsSession session = MediaSessionManager.GetCurrentSession();
         if (session != null)
         {
            Gsmtcsm_CurrentSessionChanged(MediaSessionManager, null);
         }

         MediaSessionManager.CurrentSessionChanged += Gsmtcsm_CurrentSessionChanged;
      }

      private async void Gsmtcsm_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
          CurrentSessionChangedEventArgs args)
      {
         if (CurrentSession != null)
         {
            CurrentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
            CurrentSession.PlaybackInfoChanged -= Sender_PlaybackInfoChanged;
            CurrentSession.TimelinePropertiesChanged -= Sender_TimelinePropertiesChanged;
         }

         try
         {
            CurrentSession = sender.GetCurrentSession();
            if (CurrentSession != null)
            {
               CurrentMediaProperties = await CurrentSession.TryGetMediaPropertiesAsync();
               if (CurrentMediaProperties != null)
               {
                  CurrentSession_MediaPropertiesChanged(CurrentSession, null);
               }

               CurrentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
               //CurrentSession_MediaPropertiesChanged(CurrentSession, null);
               CurrentSession.PlaybackInfoChanged += Sender_PlaybackInfoChanged;
               //Sender_PlaybackInfoChanged(CurrentSession, null);
               CurrentSession.TimelinePropertiesChanged += Sender_TimelinePropertiesChanged;
               //Sender_TimelinePropertiesChanged(CurrentSession, null);
               if (CurrentSession.GetTimelineProperties().EndTime < TimeSpan.FromSeconds(1))
               {
                  Dispatcher.UIThread.Post(() =>
                  {
                     MediaTimeLineControl.IsVisible = false;
                  });
               }
            }
         }
         catch (Exception ex)
         {
            Debug.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
            if (CurrentSession != null)
            {
               CurrentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
               CurrentSession.PlaybackInfoChanged -= Sender_PlaybackInfoChanged;
               CurrentSession.TimelinePropertiesChanged -= Sender_TimelinePropertiesChanged;
            }

            if (MediaSessionManager != null)
            {
               MediaSessionManager.CurrentSessionChanged -= Gsmtcsm_CurrentSessionChanged;
            }

            await InitializeAsync();
         }
      }

      private async void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
          MediaPropertiesChangedEventArgs args)
      {
         CurrentMediaProperties = await sender.TryGetMediaPropertiesAsync();
         UpdateMediaProperties();
      }

      private async void UpdateMediaProperties()
      {
         FadeWindowIn();
         if (CurrentMediaProperties == null)
         {
            MediaArtistTxt.Text = "";
            //MediaArtistTxt.Text = "Nothing is being played now";
            return;
         }

         if (!object.ReferenceEquals(CurrentMediaProperties.Thumbnail, null))
         {

            var image = await LoadThumbnailViaTranscodeAsync(CurrentMediaProperties.Thumbnail);
            if (image != null)
               Dispatcher.UIThread.Post(() =>
               {
                  try
                  {
                     Avalonia.Media.Imaging.Bitmap ImageSrc = image;
                     MediaImageControl.IsVisible = true;
                     MediaImageBackground.IsVisible = false;
                     MediaImageControl.Source = ImageSrc;

                  }
                  catch (Exception ex)
                  {
                     MessageBox.Show(ex.Message);
                  }
               });
         }
         else
         {
            Dispatcher.UIThread.Post(() =>
            {
               MediaImageControl.IsVisible = false;
               MediaImageBackground.IsVisible = true;

            });
         }

         Dispatcher.UIThread.Post(() =>
         {
            MediaArtistTxt.Text = CurrentMediaProperties.Artist;
            StringBuilder TitleString = new StringBuilder(CurrentMediaProperties.Title);
            for (int i = 60; i < TitleString.Length; i += 60)
            {
               TitleString.Insert(i, "\n");
            }

            SongNameTxt.Text = TitleString.ToString();
         });
      }

      private void Sender_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
          PlaybackInfoChangedEventArgs args)
      {
         CurrentPlaybackInfo = sender.GetPlaybackInfo();
         Dispatcher.UIThread.Post(() =>
         {
            var timelineMediaProps = sender.GetTimelineProperties();
            if (timelineMediaProps is not null)
               if (timelineMediaProps.EndTime < TimeSpan.FromSeconds(1))
               {
                  MediaTimeLineControl.IsVisible = false;
               }
            FadeWindowIn();
            UpdateTimelineColor();
         });
      }
      private GlobalSystemMediaTransportControlsSessionPlaybackStatus LastPlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
      private void UpdateTimelineColor()
      {
         if (CurrentPlaybackInfo is not null)
         {
            if (LastPlaybackStatus != CurrentPlaybackInfo.PlaybackStatus)
            {
               switch (CurrentPlaybackInfo.PlaybackStatus)
               {
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed:
                     MediaTimeLineControl.IsVisible = false;
                     break;
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened:
                     MediaTimeLineControl.Value = 0;
                     MediaTimeLineControl.IsVisible = false;
                     break;
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing:
                     MediaTimeLineControl.IsVisible = false;
                     break;
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped:
                     MediaTimeLineControl.Foreground = Brushes.Gray;
                     break;
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing:
                     MediaTimeLineControl.Foreground = Brushes.Green;
                     break;
                  case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused:
                     MediaTimeLineControl.Foreground = Brushes.Red;
                     break;
                  default:
                     break;
               }
               LastPlaybackStatus = CurrentPlaybackInfo.PlaybackStatus;
            }
         }
      }
      private void Sender_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
          TimelinePropertiesChangedEventArgs args)
      {
         //Progressbar min=0 max=1000
         var TimeLineProps = sender.GetTimelineProperties();
         TimeSpan TotalTime = TimeLineProps.EndTime - TimeLineProps.StartTime;
         if (TotalTime.Ticks == 0)
         {
            Dispatcher.UIThread.Post(() =>
            {
               MediaTimeLineControl.Value = 0;
               MediaTimeLineControl.IsVisible = false;
            });
         }
         else
         {
            double CurrentTimePercent = (TimeLineProps.Position.Ticks * 1000 / TotalTime.Ticks);
            Dispatcher.UIThread.Post(() =>
            {
               CurrentPlaybackInfo = sender.GetPlaybackInfo();
               UpdateTimelineColor();
               MediaTimeLineControl.Value = CurrentTimePercent / (double)10;
               MediaTimeLineControl.IsVisible = true;
            });
            //Debug.WriteLine(CurrentTimePercent);
         }
      }

      SystemMediaTransportControls smtc;
      private bool _dragging;

      private void volBorder_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
      {
         _dragging = true;

         var rect = (Control)sender!;
         var pos = e.GetPosition(rect); // relative to Rectangle

         HandlePosition(pos, rect);
         e.Pointer.Capture(rect); // optional but recommended
      }

      private void volBorder_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
      {
         if (!_dragging) return;

         var rect = (Control)sender!;
         var pos = e.GetPosition(rect);

         HandlePosition(pos, rect);
      }

      private void volBorder_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
      {
         _dragging = false;
         e.Pointer.Capture(null);
      }

      double Min = 0;
      double Max = 100;

      /// <summary>
      /// Volume 0-1
      /// </summary>
      double CurrentValue = 0;

      private void HandlePosition(Point pos, Control rect)
      {
         double height = rect.Bounds.Height;

         double normalized = 1.0 - (pos.Y / height);
         normalized = Math.Clamp(normalized, 0, 1);

         //double value = Min + normalized * (Max - Min);

         CurrentValue = normalized;
         dev.AudioEndpointVolume.MasterVolumeLevelScalar = (float)CurrentValue;

         VolBar.Height = volFullHeight * normalized;
      }
      private void TopLevel_OnOpened(object? sender, EventArgs e)
      {
         if (analyzer is null)
         {
            volFullHeight = VolBorder.Bounds.Height - 3;
            dev.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
            Task.Run(async () =>
            {
               try
               {
                  await InitializeAsync();
               }
               catch (Exception exception)
               {
                  Console.WriteLine(exception);
                  MessageBox.Show(exception.Message, "Failed to initialize media listener.");
                  throw;
               }
            });
            analyzer = new LoopbackFftAnalyzer(fftSize: VM.SettingsProperties.FftSize, bars: VM.SettingsProperties.BarCount);
            analyzer.SpectrumFrame = Analyzer_SpectrumFrame;

         }
         analyzer.Start();
         Spectrum.PauseRendering = false;

      }

      private void Analyzer_SpectrumFrame(float[] bars)
      {
         Avalonia.Threading.Dispatcher.UIThread.Post(() =>
         {
            Spectrum.Data = bars;
            Spectrum.InvalidateVisual();
            DataRate.Text = analyzer.DataRate.ToString();
         });
      }

      private void TopLevel_OnClosed(object? sender, EventArgs e)
      {
         dev.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
      }

      public static async Task<Bitmap?> LoadThumbnailViaTranscodeAsync(IRandomAccessStreamReference? thumbRef)
      {
         if (thumbRef is null) return null;

         using IRandomAccessStream ras = await thumbRef.OpenReadAsync();
         var decoder = await BitmapDecoder.CreateAsync(ras);

         // Get pixels in a standard format
         var pixelData = await decoder.GetPixelDataAsync(
             BitmapPixelFormat.Bgra8,
             BitmapAlphaMode.Premultiplied,
             new BitmapTransform(),
             ExifOrientationMode.IgnoreExifOrientation,
             ColorManagementMode.DoNotColorManage);

         byte[] pixels = pixelData.DetachPixelData();

         // Encode into a new PNG in memory
         using var outStream = new InMemoryRandomAccessStream();
         var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);

         encoder.SetPixelData(
             BitmapPixelFormat.Bgra8,
             BitmapAlphaMode.Premultiplied,
             decoder.PixelWidth,
             decoder.PixelHeight,
             96, 96,
             pixels);

         await encoder.FlushAsync();

         // Convert to .NET stream for Avalonia
         outStream.Seek(0);
         using var dotnet = outStream.AsStreamForRead();
         using var ms = new MemoryStream();
         await dotnet.CopyToAsync(ms);
         //ms.Position = 0;
         //await ms.CopyToAsync(File.OpenWrite(@"C:\Users\Danial\Desktop\meaningless.jpg"));
         ms.Position = 0;
         return new Bitmap(ms);
      }

      private void Window_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
      {

      }
      private void Grid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
      {
         if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
         {
            this.BeginMoveDrag(e);
         }
         else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
         {
            OpenSettingsWindow();
         }
      }
      public void OpenSettingsWindow()
      {
         if (VM.SettingsProperties.IsSettingsWindowOpen && SettingsWindow is not null)
         {
            SettingsWindow.Activate();
            return;
         }
         VM.SettingsProperties.IsSettingsWindowOpen = !VM.SettingsProperties.IsSettingsWindowOpen;
         SettingsWindow = new(VM.SettingsProperties);
         SettingsWindow.Show(this);
         SettingsWindow.Closed += SettingsWindow_Closed;
      }

      private void SettingsWindow_Closed(object? sender, EventArgs e)
      {
         VM.SettingsProperties.IsSettingsWindowOpen = false;
         RescheduleTimer();
      }

      private void Grid_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
      {

      }

      private void VolBorder_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
      {
         CurrentValue = Math.Max(Math.Min((e.Delta.Y / 25f) + CurrentValue, 1), 0);

         VolBar.Height = volFullHeight * CurrentValue;
         dev.AudioEndpointVolume.MasterVolumeLevelScalar = (float)CurrentValue;

      }

      private void Window_PositionChanged(object? sender, PixelPointEventArgs e)
      {
         if (!this.IsLoaded) return;

         if (!VM.SettingsProperties.WindowPosX.Equals(Position.X))
            VM.SettingsProperties.WindowPosX = Position.X;
         if (!VM.SettingsProperties.WindowPosY.Equals(Position.Y))
            VM.SettingsProperties.WindowPosY = Position.Y;
      }

      private async void LoadAsync(object? sender, RoutedEventArgs e)
      {

      }

      // Save helper that uses the matching options used to load
      private async Task SaveSettingsAsync()
      {
         try
         {
            using var fs = File.Create("config.json");
            var options = new JsonSerializerOptions
            {
               WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            await JsonSerializer.SerializeAsync(fs, VM.SettingsProperties, options);
            await fs.FlushAsync();
         }
         catch (Exception ex)
         {
            MessageBox.Show(ex.Message);
         }
      }

      private async void Window_Closing(object? sender, WindowClosingEventArgs e)
      {
         // ensure we save with the same options used for reading
         await SaveSettingsAsync();
      }
   }
}
