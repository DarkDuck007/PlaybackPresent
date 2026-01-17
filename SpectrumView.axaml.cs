using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;

namespace PlaybackPresent;

public partial class SpectrumView : UserControl
{
   public float[] Data { get; set; } = Array.Empty<float>();

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
      var barWidth = bounds.Width / Data.Length;

      for (int i = 0; i < Data.Length; i++)
      {
         var h = bounds.Height * Data[i];
         context.FillRectangle(
            BarBrush,
             new Rect(i * barWidth, bounds.Height - h, barWidth - BarDistance, h));
      }
   }

   public void Invalidate() => InvalidateVisual();

}