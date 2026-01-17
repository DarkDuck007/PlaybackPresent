using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.ComponentModel.Design;


namespace PlaybackPresent;

public class NAudioHelper
{

}


public sealed class LoopbackFftAnalyzer : IDisposable
{
   int DataCount = 0;
   public int DataRate { get; private set; } = 0;
   private readonly WasapiLoopbackCapture _capture;
   DateTime LastDataTime = DateTime.Now;

   private FftAggregator _fft;

   public Action<float[]>? SpectrumFrame; // bars 0..1

   bool PauseUpdates = false;
   public void SetNewFftAggregator(int fftSize = 2048, int bars = 32)
   {
      //Stop();
      PauseUpdates = true;
      _fft = new FftAggregator(fftSize, bars, _capture.WaveFormat.SampleRate);
      PauseUpdates = false;
      // Start();
   }
   public LoopbackFftAnalyzer(int fftSize = 2048, int bars = 32)
   {
      _capture = new WasapiLoopbackCapture(); // default render device
      _fft = new FftAggregator(fftSize, bars, _capture.WaveFormat.SampleRate);

      _capture.DataAvailable += (s, e) =>
      {
         if (PauseUpdates)
            return;
         if ((DateTime.Now - LastDataTime) > TimeSpan.FromSeconds(1))
         {
            DataRate = (int)(DataCount * (1 / (DateTime.Now - LastDataTime).TotalSeconds));
            LastDataTime = DateTime.Now;
            DataCount = 0;
         }
         DataCount++;
         // e.Buffer is PCM in WaveFormat of capture
         var barsArr = _fft.AddSamples(e.Buffer, 0, e.BytesRecorded, _capture.WaveFormat);
         if (barsArr != null)
            SpectrumFrame?.Invoke(barsArr);
      };
   }


   public bool Start()
   {
      if (_capture.CaptureState == CaptureState.Capturing)
      {
         return false;
      }
      else
      {
         _capture.StartRecording();
         return true;
      }
   }
   public void Stop() => _capture.StopRecording();

   public void Dispose()
   {
      Stop();
      _capture.Dispose();
   }
}


public sealed class FftAggregator
{
   private readonly (int left, int center, int right)[] _filters;

   private readonly int _fftSize;
   private readonly int _m; // log2(fftSize)
   private readonly Complex[] _fftBuffer;
   private int _fftPos;

   private readonly int _bars;
   private readonly int _sampleRate;

   // smoothing state
   private readonly float[] _lastBars;
   private readonly float[] _barsBuffer;

   // window coefficients (Hann)
   private readonly float[] _window;

   public FftAggregator(int fftSize, int bars, int sampleRate)
   {
      if ((fftSize & (fftSize - 1)) != 0) throw new ArgumentException("fftSize must be power of 2");
      _fftSize = fftSize;
      _m = (int)Math.Log2(fftSize);
      _fftBuffer = new Complex[fftSize];
      _fftPos = 0;

      _bars = bars;
      _sampleRate = sampleRate;

      _lastBars = new float[bars];
      _barsBuffer = new float[bars];

      _window = new float[fftSize];

      double minFreq = 20;
      double maxFreq = Math.Min(18000, _sampleRate / 2.0);

      _filters = BuildLogTriFilters(
          bars: _bars,
          sampleRate: _sampleRate,
          fftSize: _fftSize,
          minHz: minFreq,
          maxHz: maxFreq);

      for (int i = 0; i < fftSize; i++)
      {
         // Hann window
         _window[i] = 0.5f * (1f - (float)Math.Cos(2 * Math.PI * i / (fftSize - 1)));
      }
   }

   /// <summary>
   /// Returns float[bars] (0..1) only when an FFT frame is ready; otherwise null.
   /// </summary>
   public float[]? AddSamples(byte[] buffer, int offset, int count, WaveFormat format)
   {
      // Convert incoming buffer to mono float samples
      // Loopback usually gives IEEE float stereo.
      int bytesPerSample = format.BitsPerSample / 8;
      int channels = format.Channels;

      if (format.Encoding == WaveFormatEncoding.IeeeFloat)
      {
         int samples = count / 4; // float = 4 bytes
         for (int n = 0; n < samples; n += channels)
         {
            float mono = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
               mono += BitConverter.ToSingle(buffer, offset + 4 * (n + ch));
            }
            mono /= channels;

            var bars = AddMonoSample(mono);
            if (bars != null) return bars;
         }
      }
      else if (format.Encoding == WaveFormatEncoding.Pcm)
      {
         // handle 16-bit PCM common cases
         if (format.BitsPerSample == 16)
         {
            int samples = count / 2;
            for (int n = 0; n < samples; n += channels)
            {
               float mono = 0f;
               for (int ch = 0; ch < channels; ch++)
               {
                  short s = BitConverter.ToInt16(buffer, offset + 2 * (n + ch));
                  mono += s / 32768f;
               }
               mono /= channels;

               var bars = AddMonoSample(mono);
               if (bars != null) return bars;
            }
         }
         else
         {
            // You can add 24/32-bit PCM support if needed
            throw new NotSupportedException($"PCM {format.BitsPerSample}-bit not implemented in this snippet.");
         }
      }
      else
      {
         throw new NotSupportedException($"Unsupported encoding: {format.Encoding}");
      }

      return null;
   }

   private float[]? AddMonoSample(float sample)
   {
      // Apply window as we fill (store in Complex.X)
      _fftBuffer[_fftPos].X = sample * _window[_fftPos];
      _fftBuffer[_fftPos].Y = 0;
      _fftPos++;

      if (_fftPos < _fftSize) return null;

      _fftPos = 0;

      // FFT in-place
      FastFourierTransform.FFT(true, _m, _fftBuffer);

      // Magnitudes (only first half is useful)
      int usefulBins = _fftSize / 2;

      var bars = _barsBuffer;

      for (int b = 0; b < _bars; b++)
      {
         //// take max magnitude in the bin range (looks punchier than average)
         //double maxMag = 0;
         //for (int i = bin0; i < bin1 && i < usefulBins; i++)
         //{
         //   double re = _fftBuffer[i].X;
         //   double im = _fftBuffer[i].Y;
         //   double mag = Math.Sqrt(re * re + im * im);
         //   if (mag > maxMag) maxMag = mag;
         //}

         //// Convert to dB-ish and normalize
         //// small epsilon to avoid log(0)
         //double db = 20.0 * Math.Log10(maxMag + 1e-9);

         //V2: power spectrum average with triangular filter
         var (l, c, r) = _filters[b];

         double sum = 0;
         double wsum = 0;

         for (int i = l; i <= r && i < usefulBins; i++)
         {
            double re = _fftBuffer[i].X;
            double im = _fftBuffer[i].Y;

            // power spectrum (better than magnitude)
            double p = re * re + im * im;
            // magnitude spectrum
            //double mag = Math.Sqrt(re * re + im * im);

            double w = i <= c
                ? (i - l) / (double)Math.Max(1, c - l)
                : (r - i) / (double)Math.Max(1, r - c);

            sum += p * w;
            wsum += w;
         }

         double power = (wsum > 0) ? (sum / wsum) : 0;

         // dB scale
         double db = 10.0 * Math.Log10(power + 1e-20);




         // Typical useful range for visuals (tune these)
         // -80 dB => 0, 0 dB => 1
         float norm = (float)((db + 80.0) / 80.0);
         norm = Math.Clamp(norm, 0f, 1f);

         // Smoothing/decay (fast rise, slower fall)
         float prev = _lastBars[b];
         float rise = 0.8f;
         float fall = 0.7f;
         float smoothed = norm > prev
             ? prev + (norm - prev) * rise
             : prev + (norm - prev) * fall;

         _lastBars[b] = smoothed;
         bars[b] = smoothed;
      }

      return bars;
   }

   static (int left, int center, int right)[] BuildLogTriFilters(
    int bars, int sampleRate, int fftSize, double minHz, double maxHz)
   {
      int usefulBins = fftSize / 2;
      double nyquist = sampleRate / 2.0;

      int FreqToBin(double f)
          => Math.Clamp((int)Math.Round((f / nyquist) * (usefulBins - 1)), 0, usefulBins - 1);

      // Log-spaced *centers* with extra points for left/right
      double Ratio(double t) => minHz * Math.Pow(maxHz / minHz, t);

      var pts = new int[bars + 2];
      for (int i = 0; i < pts.Length; i++)
         pts[i] = FreqToBin(Ratio(i / (double)(bars + 1)));

      // Ensure strictly increasing bins (VERY important for low freq)
      for (int i = 1; i < pts.Length; i++)
         if (pts[i] <= pts[i - 1]) pts[i] = Math.Min(usefulBins - 1, pts[i - 1] + 1);

      var filters = new (int, int, int)[bars];
      for (int b = 0; b < bars; b++)
         filters[b] = (pts[b], pts[b + 1], pts[b + 2]);

      return filters;
   }

}
