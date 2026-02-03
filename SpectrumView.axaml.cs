using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;

namespace PlaybackPresent;

public partial class SpectrumView : UserControl
{
   public float[] LeftData { get; set; } = Array.Empty<float>();
   public float[] RightData { get; set; } = Array.Empty<float>();

   public int BarDistance { get; set; } = 1;

   public static readonly StyledProperty<IBrush> BarBrushProperty =
       AvaloniaProperty.Register<SpectrumView, IBrush>(nameof(BarBrush), Brushes.Magenta);
   public IBrush BarBrush
   {
      get => GetValue(BarBrushProperty);
      set => SetValue(BarBrushProperty, value);
   }
   public bool PauseRendering { get; set; } = false;
   public SpectrumView()
   {
      InitializeComponent();
   }

   public override void Render(DrawingContext context)
   {
      base.Render(context);
      if (PauseRendering)
         return;
      var bounds = Bounds;
      if (LeftData.Length == 0 || RightData.Length == 0)
         return;

      int bars = Math.Min(LeftData.Length, RightData.Length);
      if (bars == 0)
         return;

      var halfWidth = bounds.Width / 2.0;
      var barWidth = halfWidth / bars;

      for (int i = 0; i < bars; i++)
      {
         var h = bounds.Height * LeftData[i];
         context.FillRectangle(
            BarBrush,
            new Rect(i * barWidth, bounds.Height - h, barWidth - BarDistance, h));
      }

      for (int i = 0; i < bars; i++)
      {
         var h = bounds.Height * RightData[i];
         var x = bounds.Width - (i + 1) * barWidth;
         context.FillRectangle(
            BarBrush,
            new Rect(x, bounds.Height - h, barWidth - BarDistance, h));
      }
   }

   public void Invalidate() => InvalidateVisual();

}
