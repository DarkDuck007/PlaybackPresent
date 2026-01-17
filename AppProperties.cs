using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PlaybackPresent
{
   [JsonSerializable(typeof(AppProperties))]
   public class AppProperties
   {
      public bool FirstRun { get; set; } = true;
      public bool StartWithWindows { get; set; } = false;
      public bool OpenMinimized { get; set; } = true;
      public bool EnableSpectrumVisualizer { get; set; } = true;
      //public int SpectrumBarCount { get; set; } = 64;
      public double PopupWidth { get; set; } = 400;

      public SerializableGradient SpectrumGradient { get; set; } = new SerializableGradient();
   }

   [JsonSerializable(typeof(SerializableGradient))]
   public class SerializableGradient
   {
      GradientType Type { get; set; } = GradientType.Linear;
      public List<SerializableGradientStop> GradientStops { get; set; } = new();
   }
   [JsonSerializable(typeof(SerializableGradientStop))]
   public class SerializableGradientStop
   {
      public string ColorHex { get; set; } = "#FFFFFFFF";
      public double Offset { get; set; } = 0.0;
   }

   public enum GradientType
   {
      Linear,
      Radial,
      Conical
   }
}
