This project is an Avalonia-based replacement for the Windows volume flyout. It listens to the default render endpoint for volume changes, shows a custom volume UI, and optionally renders a real-time audio spectrum. It also integrates with Windows media sessions to show track art/artist/title and playback status, and exposes a settings window for window size/position, auto-hide timeout, FFT size/bar count, and spectrum gradient styling.

Performance optimization ideas (targeting FFT/spectrum GC pressure, not a full cleanup):
- Reuse the spectrum bars buffer: allocate a single `float[]` of size `BarCount` in `FftAggregator` and fill it in-place per frame, instead of `new float[_bars]` each FFT. Expose it as a shared buffer (or copy into a pooled buffer before raising `SpectrumFrame`).
- Use `ArrayPool<float>` if you must hand off arrays to the UI; return the buffer when the frame is consumed to avoid per-frame allocations.
- Precompute bar bin ranges/weights once in `BuildLogTriFilters` and remove per-frame min/max/bin math in `AddMonoSample` (you already compute filters; use them directly and drop the repeated log spacing work).
- Replace `BitConverter.ToSingle/ToInt16` per-sample with `MemoryMarshal.Cast<byte, float>` / `Span<short>` to avoid per-sample overhead and bounds checks (keeps allocations flat).
- Avoid resetting `Spectrum.Data` with `new float[0]`; use `Array.Empty<float>()` and keep a single empty array instance.
- Keep `Spectrum.Data` pointing at a stable buffer and just update its contents; invalidate the control to redraw, but avoid swapping arrays each frame.
