//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Renders what a high speed camera filming the screen of a SyntheticScenario records: the TopLeft and BottomLeft markers seen through a
//* perspective transform and lens, with a rolling scanout (row y shows a new frame y/height of the scanout after vsync), panel response,
//* exposure blending, blur, noise and a drifting camera clock. It is the ground truth for the camera calibration, rectification and analysis.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed class SyntheticCamera
  {
    private readonly Zone[] m_zones;
    private readonly byte[] m_base;
    private readonly float[] m_scratch;
    private readonly float[] m_horizontal;
    private readonly Dictionary<int, ModuleMatrix> m_matrices = new Dictionary<int, ModuleMatrix>();

    public SyntheticCamera(SyntheticScenario scenario, SyntheticCameraOptions options)
    {
      Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
      Options = options ?? throw new ArgumentNullException(nameof(options));
      var o = options;
      int frameMarker = MarkerRenderer.MarkerSizePx(o.ModuleSizePx);
      if (o.InsetPx + frameMarker > o.ScreenWidth || (2 * (o.InsetPx + frameMarker)) > o.ScreenHeight)
        throw new ArgumentException($"A {o.ScreenWidth}x{o.ScreenHeight} screen is too small for two {frameMarker}px markers");

      ScreenToCamera = o.ScreenToCamera ?? DefaultScreenToCamera(o);
      if (!ScreenToCamera.TryInvert(out var cameraToScreen))
        throw new ArgumentException("The screen to camera transform is singular", nameof(options));

      var markers = new List<PixelRect>
      {
        new PixelRect(o.InsetPx, o.InsetPx, frameMarker, frameMarker),
        new PixelRect(o.InsetPx, o.ScreenHeight - o.InsetPx - frameMarker, frameMarker, frameMarker),
      };
      if (o.MiddleMarker)
        markers.Add(new PixelRect(o.InsetPx, (o.ScreenHeight - frameMarker) / 2, frameMarker, frameMarker));
      ZoneMarkers = markers;
      m_zones = new Zone[markers.Count];
      m_base = new byte[o.CameraWidth * o.CameraHeight];
      m_scratch = new float[o.CameraWidth * o.CameraHeight];
      m_horizontal = new float[o.CameraWidth * o.CameraHeight];
      for (int z = 0; z < ZoneCount; ++z)
        m_zones[z] = new Zone(ZoneMarkers[z].Y, o.ModuleSizePx, o.TimeSamples);
      Precompute(cameraToScreen);
    }

    public SyntheticScenario Scenario { get; }

    public SyntheticCameraOptions Options { get; }

    /// <summary>The screen to camera transform without lens distortion.</summary>
    public Homography ScreenToCamera { get; }

    /// <summary>
    /// Frame marker bounds (including the quiet zone) in screen pixels: [0] the TopLeft slot, [1] the BottomLeft slot, [2] the MiddleLeft slot
    /// when <see cref="SyntheticCameraOptions.MiddleMarker"/> is set.
    /// </summary>
    public IReadOnlyList<PixelRect> ZoneMarkers { get; }

    public int ZoneCount => ZoneMarkers.Count;

    public long CaptureCount => Scenario.CaptureCount;

    /// <summary>The true time the exposure of a capture starts, in display clock ticks.</summary>
    public double TrueTicks(long captureIndex) =>
      (captureIndex + Scenario.Options.CapturePhase) * TimeSpan.TicksPerSecond / Scenario.Options.CaptureFps;

    /// <summary>The timestamp the camera writes for a capture (its own, drifting clock).</summary>
    public long CameraTicks(long captureIndex) => (long)Math.Round(TrueTicks(captureIndex) * (1 + (Options.ClockDriftPpm * 1e-6)));

    /// <summary>Converts a display clock time to the camera clock.</summary>
    public double ToCameraTicks(double displayTicks) => displayTicks * (1 + (Options.ClockDriftPpm * 1e-6));

    /// <summary>Time from vsync until the scanout has drawn the whole frame marker of a zone (its bottom symbol row), in display clock ticks.</summary>
    public double ZoneScanTicks(int zone)
    {
      double bottomRow =
        ZoneMarkers[zone].Y + ((MarkerRenderer.RecommendedQuietZoneModules + MarkerRenderer.FrameQrModuleCount) * (double)Options.ModuleSizePx);
      return bottomRow / Options.ScreenHeight * ScanoutTicks;
    }

    /// <summary>The true module to camera transform of a zone (without lens distortion).</summary>
    public Homography ZoneModuleToCamera(int zone)
    {
      double symbolX = ZoneMarkers[zone].X + (MarkerRenderer.RecommendedQuietZoneModules * Options.ModuleSizePx);
      double symbolY = ZoneMarkers[zone].Y + (MarkerRenderer.RecommendedQuietZoneModules * Options.ModuleSizePx);
      var moduleToScreen = new Homography(Options.ModuleSizePx, 0, symbolX, 0, Options.ModuleSizePx, symbolY, 0, 0);
      return Homography.Multiply(ScreenToCamera, moduleToScreen);
    }

    /// <summary>Where a module coordinate of a zone appears in the recorded camera frame, lens distortion included.</summary>
    public ImagePoint ZoneModuleToObserved(int zone, ImagePoint module) => IdealToObserved(ZoneModuleToCamera(zone).Map(module));

    /// <summary>Inverse of the lens model (observed -> ideal scales by 1 + k r²), solved by fixed point iteration.</summary>
    private ImagePoint IdealToObserved(ImagePoint ideal)
    {
      var o = Options;
      double cx = o.CameraWidth / 2.0;
      double cy = o.CameraHeight / 2.0;
      double halfDiagonal2 = ((o.CameraWidth * o.CameraWidth) + (o.CameraHeight * o.CameraHeight)) / 4.0;
      double ix = ideal.X - cx;
      double iy = ideal.Y - cy;
      double ox = ix;
      double oy = iy;
      for (int i = 0; i < 50; ++i)
      {
        double factor = 1 + (o.LensDistortion * ((ox * ox) + (oy * oy)) / halfDiagonal2);
        ox = ix / factor;
        oy = iy / factor;
      }
      return new ImagePoint(ox + cx, oy + cy);
    }

    private double ScanoutTicks => Options.ScanoutFraction * Scenario.RefreshIntervalTicks;

    /// <summary>Render capture <paramref name="captureIndex"/> into <paramref name="target"/> (camera sized).</summary>
    public void Render(long captureIndex, GrayImage target)
    {
      var o = Options;
      if (target.Width != o.CameraWidth || target.Height != o.CameraHeight)
        throw new ArgumentException("The target must have the camera size", nameof(target));

      double start = TrueTicks(captureIndex);
      double exposure = o.ExposureFraction * TimeSpan.TicksPerSecond / Scenario.Options.CaptureFps;
      PruneMatrices(captureIndex);
      foreach (var zone in m_zones)
        UpdateRows(zone, start, exposure);

      for (int i = 0; i < m_base.Length; ++i)
        m_scratch[i] = m_base[i];
      foreach (var zone in m_zones)
        RenderZone(zone);

      BlurAndStore(target, captureIndex);
    }

    /// <summary>Which frame each screen row of a zone shows at every time sample of the exposure, and how far the panel has switched.</summary>
    private void UpdateRows(Zone zone, double start, double exposure)
    {
      double refresh = Scenario.RefreshIntervalTicks;
      double tau = Options.PanelResponseSeconds * TimeSpan.TicksPerSecond;
      for (int s = 0; s < Options.TimeSamples; ++s)
      {
        double t = start + ((s + 0.5) / Options.TimeSamples * exposure);
        for (int r = 0; r < zone.RowCount; ++r)
        {
          double offset = (zone.RowMin + r + 0.5) / Options.ScreenHeight * ScanoutTicks;
          double scan = (Math.Floor((t - offset) / refresh) * refresh) + offset;
          int current = Scenario.PresentedIndexAtTicks((long)Math.Floor(scan));
          int previous = Scenario.PresentedIndexAtTicks((long)Math.Floor(scan - refresh));
          int index = (s * zone.RowCount) + r;
          zone.Current[index] = MatrixFor(current);
          zone.Previous[index] = MatrixFor(previous);
          zone.Weight[index] = current == previous || tau <= 0 ? 0f : (float)Math.Exp(-(t - scan) / tau);
        }
      }
    }

    private void RenderZone(Zone zone)
    {
      var o = Options;
      int timeSamples = o.TimeSamples;
      float samplesPerPixel = o.SpatialSamples * o.SpatialSamples * timeSamples;
      Parallel.For(
        0,
        zone.PixelCount,
        pixel =>
        {
          float sum = zone.ConstantSum[pixel] * timeSamples;
          for (int k = zone.SampleStart[pixel]; k < zone.SampleStart[pixel + 1]; ++k)
          {
            int row = zone.SampleRow[k];
            int mx = zone.SampleModuleX[k];
            int my = zone.SampleModuleY[k];
            for (int s = 0; s < timeSamples; ++s)
            {
              int index = (s * zone.RowCount) + row;
              float now = Luma(zone.Current[index], mx, my);
              float weight = zone.Weight[index];
              sum += weight > 0.002f ? now + ((Luma(zone.Previous[index], mx, my) - now) * weight) : now;
            }
          }
          m_scratch[zone.PixelOffset[pixel]] = sum / samplesPerPixel;
        }
      );
    }

    private float Luma(ModuleMatrix? matrix, int mx, int my)
    {
      if (matrix == null)
        return Options.SceneLuma;
      int size = matrix.Size;
      if ((uint)mx < (uint)size && (uint)my < (uint)size)
        return matrix.IsDark(mx, my) ? Options.ScreenBlack : Options.ScreenWhite;
      int quiet = MarkerRenderer.RecommendedQuietZoneModules;
      return mx >= -quiet && mx < size + quiet && my >= -quiet && my < size + quiet ? Options.ScreenWhite : Options.SceneLuma;
    }

    private void BlurAndStore(GrayImage target, long captureIndex)
    {
      var o = Options;
      int width = o.CameraWidth;
      int height = o.CameraHeight;
      var kernel = GaussianKernel(o.BlurSigma);
      int radius = kernel.Length / 2;
      var horizontal = m_horizontal;
      Parallel.For(
        0,
        height,
        y =>
        {
          for (int x = 0; x < width; ++x)
          {
            float sum = 0;
            for (int k = -radius; k <= radius; ++k)
              sum += kernel[k + radius] * m_scratch[(y * width) + Math.Clamp(x + k, 0, width - 1)];
            horizontal[(y * width) + x] = sum;
          }
        }
      );
      ulong seed = (ulong)o.Seed * 0x9E3779B97F4A7C15UL;
      Parallel.For(
        0,
        height,
        y =>
        {
          for (int x = 0; x < width; ++x)
          {
            float sum = 0;
            for (int k = -radius; k <= radius; ++k)
              sum += kernel[k + radius] * horizontal[(Math.Clamp(y + k, 0, height - 1) * width) + x];
            double noise = o.NoiseSigma > 0 ? o.NoiseSigma * Gaussianish(seed ^ (ulong)captureIndex, (ulong)((y * width) + x)) : 0;
            target.Pixels[(y * target.Stride) + x] = (byte)Math.Clamp(Math.Round(sum + noise), 0, 255);
          }
        }
      );
    }

    private static float[] GaussianKernel(double sigma)
    {
      if (sigma <= 0)
        return new[] { 1f };
      int radius = (int)Math.Ceiling(3 * sigma);
      var kernel = new float[(2 * radius) + 1];
      double total = 0;
      for (int i = -radius; i <= radius; ++i)
      {
        double value = Math.Exp(-(i * i) / (2 * sigma * sigma));
        kernel[i + radius] = (float)value;
        total += value;
      }
      for (int i = 0; i < kernel.Length; ++i)
        kernel[i] = (float)(kernel[i] / total);
      return kernel;
    }

    /// <summary>A reproducible, roughly normal value (mean 0, deviation 1) from a hash of the capture and pixel.</summary>
    private static double Gaussianish(ulong capture, ulong pixel)
    {
      ulong h = (capture * 0xD1B54A32D192ED03UL) ^ (pixel * 0x9E3779B97F4A7C15UL);
      double sum = 0;
      for (int i = 0; i < 4; ++i)
      {
        h ^= h >> 31;
        h *= 0x7FB5D329728EA185UL;
        h ^= h >> 27;
        sum += (h >> 11) * (1.0 / (1UL << 53));
      }
      // Sum of 4 uniforms: mean 2, variance 4/12
      return (sum - 2) * Math.Sqrt(3);
    }

    private ModuleMatrix? MatrixFor(int presentedIndex)
    {
      if (presentedIndex < 0)
        return null;
      lock (m_matrices)
      {
        if (!m_matrices.TryGetValue(presentedIndex, out var matrix))
        {
          var payload = Scenario.PresentedFrames[presentedIndex].Payload;
          matrix = MarkerRenderer.GenerateModules(payload, payload.Kind == MarkerKind.SequenceStart ? Scenario.StartMetadata : null);
          m_matrices.Add(presentedIndex, matrix);
        }
        return matrix;
      }
    }

    private void PruneMatrices(long captureIndex)
    {
      if (m_matrices.Count < 64)
        return;
      int oldest = Scenario.PresentedIndexAtTicks((long)(TrueTicks(captureIndex) - (4 * Scenario.RefreshIntervalTicks)));
      var stale = new List<int>();
      foreach (var key in m_matrices.Keys)
      {
        if (key < oldest)
          stale.Add(key);
      }
      foreach (var key in stale)
        m_matrices.Remove(key);
    }

    /// <summary>Which screen positions every camera pixel sees: constant (scene, surround) parts are summed once, marker samples listed.</summary>
    private void Precompute(Homography cameraToScreen)
    {
      var o = Options;
      int quiet = MarkerRenderer.RecommendedQuietZoneModules;
      int zoneSize = MarkerRenderer.MaxMarkerSizePx(o.ModuleSizePx);
      int n = o.SpatialSamples;
      double halfDiagonal = Math.Sqrt((o.CameraWidth * o.CameraWidth) + (o.CameraHeight * o.CameraHeight)) / 2;
      var builders = new List<ZoneBuilder>();
      for (int z = 0; z < ZoneCount; ++z)
        builders.Add(new ZoneBuilder());

      for (int y = 0; y < o.CameraHeight; ++y)
      {
        for (int x = 0; x < o.CameraWidth; ++x)
        {
          int zoneOfPixel = -1;
          float constant = 0;
          var samples = new List<(int Row, int Mx, int My)>();
          for (int sy = 0; sy < n; ++sy)
          {
            for (int sx = 0; sx < n; ++sx)
            {
              // Observed (distorted) camera position -> ideal pinhole position -> screen
              double cx = x + ((sx + 0.5) / n) - (o.CameraWidth / 2.0);
              double cy = y + ((sy + 0.5) / n) - (o.CameraHeight / 2.0);
              double r2 = ((cx * cx) + (cy * cy)) / (halfDiagonal * halfDiagonal);
              double factor = 1 + (o.LensDistortion * r2);
              var screen = cameraToScreen.Map(new ImagePoint((cx * factor) + (o.CameraWidth / 2.0), (cy * factor) + (o.CameraHeight / 2.0)));
              if (!(screen.X >= 0 && screen.Y >= 0 && screen.X < o.ScreenWidth && screen.Y < o.ScreenHeight))
              {
                constant += o.SurroundLuma;
                continue;
              }
              // A zone's frame marker wins over another zone's start marker area (which only matters while a start marker shows)
              int zone = -1;
              for (int z = 0; z < ZoneCount && zone < 0; ++z)
              {
                var marker = ZoneMarkers[z];
                if (screen.X >= marker.X && screen.Y >= marker.Y && screen.X < marker.Right && screen.Y < marker.Bottom)
                  zone = z;
              }
              for (int z = 0; z < ZoneCount && zone < 0; ++z)
              {
                var origin = ZoneMarkers[z];
                if (screen.X >= origin.X && screen.Y >= origin.Y && screen.X < origin.X + zoneSize && screen.Y < origin.Y + zoneSize)
                  zone = z;
              }
              if (zone < 0 || (zoneOfPixel >= 0 && zone != zoneOfPixel))
              {
                constant += o.SceneLuma;
                continue;
              }
              zoneOfPixel = zone;
              var zoneOrigin = ZoneMarkers[zone];
              int mx = (int)Math.Floor((screen.X - zoneOrigin.X) / o.ModuleSizePx) - quiet;
              int my = (int)Math.Floor((screen.Y - zoneOrigin.Y) / o.ModuleSizePx) - quiet;
              samples.Add(((int)Math.Floor(screen.Y), mx, my));
            }
          }
          int offset = (y * o.CameraWidth) + x;
          if (zoneOfPixel < 0)
          {
            m_base[offset] = (byte)Math.Round(constant / (n * n));
            continue;
          }
          builders[zoneOfPixel].Add(offset, constant, samples);
        }
      }
      for (int z = 0; z < ZoneCount; ++z)
        builders[z].Build(m_zones[z]);
    }

    private static Homography DefaultScreenToCamera(SyntheticCameraOptions o)
    {
      // The camera frames the left 45% of the screen, seen a little from the right and below, slightly rotated
      double visible = o.ScreenWidth * 0.45;
      var screen = new[]
      {
        new ImagePoint(0, 0),
        new ImagePoint(visible, 0),
        new ImagePoint(0, o.ScreenHeight),
        new ImagePoint(visible, o.ScreenHeight),
      };
      double w = o.CameraWidth;
      double h = o.CameraHeight;
      var camera = new[]
      {
        new ImagePoint(w * 0.06, h * 0.03),
        new ImagePoint(w * 0.95, h * 0.06),
        new ImagePoint(w * 0.07, h * 0.98),
        new ImagePoint(w * 0.93, h * 0.94),
      };
      if (!Homography.TryFromPoints(screen, camera, out var homography))
        throw new InvalidOperationException("The default camera transform is singular");
      return homography;
    }

    private sealed class Zone
    {
      public Zone(int originY, int moduleSizePx, int timeSamples)
      {
        RowMin = originY;
        RowCount = MarkerRenderer.MaxMarkerSizePx(moduleSizePx);
        Current = new ModuleMatrix?[timeSamples * RowCount];
        Previous = new ModuleMatrix?[timeSamples * RowCount];
        Weight = new float[timeSamples * RowCount];
      }

      public int RowMin { get; }
      public int RowCount { get; }
      public ModuleMatrix?[] Current { get; }
      public ModuleMatrix?[] Previous { get; }
      public float[] Weight { get; }

      public int PixelCount { get; set; }
      public int[] PixelOffset { get; set; } = Array.Empty<int>();
      public float[] ConstantSum { get; set; } = Array.Empty<float>();
      public int[] SampleStart { get; set; } = Array.Empty<int>();
      public int[] SampleRow { get; set; } = Array.Empty<int>();
      public int[] SampleModuleX { get; set; } = Array.Empty<int>();
      public int[] SampleModuleY { get; set; } = Array.Empty<int>();
    }

    private sealed class ZoneBuilder
    {
      private readonly List<int> m_offsets = new List<int>();
      private readonly List<float> m_constants = new List<float>();
      private readonly List<int> m_starts = new List<int>();
      private readonly List<(int Row, int Mx, int My)> m_samples = new List<(int Row, int Mx, int My)>();

      public void Add(int offset, float constant, List<(int Row, int Mx, int My)> samples)
      {
        m_offsets.Add(offset);
        m_constants.Add(constant);
        m_starts.Add(m_samples.Count);
        m_samples.AddRange(samples);
      }

      public void Build(Zone zone)
      {
        zone.PixelCount = m_offsets.Count;
        zone.PixelOffset = m_offsets.ToArray();
        zone.ConstantSum = m_constants.ToArray();
        m_starts.Add(m_samples.Count);
        zone.SampleStart = m_starts.ToArray();
        zone.SampleRow = new int[m_samples.Count];
        zone.SampleModuleX = new int[m_samples.Count];
        zone.SampleModuleY = new int[m_samples.Count];
        for (int i = 0; i < m_samples.Count; ++i)
        {
          zone.SampleRow[i] = m_samples[i].Row - zone.RowMin;
          zone.SampleModuleX[i] = m_samples[i].Mx;
          zone.SampleModuleY[i] = m_samples[i].My;
        }
      }
    }
  }
}
