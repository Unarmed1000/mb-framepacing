//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Calibrates and verifies a camera rig (EXPERIMENTAL camera support). The application draws the same marker in the TopLeft and BottomLeft
//* slots; the camera films both. Calibration finds each marker, fits its module to camera transform (detector points, then a refinement
//* against the known module pattern), measures how much later the scanout reaches the second zone and the refresh rate, and checks module
//* size, focus, exposure, flicker and stability. Verification only checks that both markers are still where the rig says.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Camera
{
  public static class CameraCalibrator
  {
    public const int RequiredZones = 2;

    /// <summary>Below this many camera pixels per module decoding is unreliable; below the recommended size it is marginal.</summary>
    public const double MinimumModulePx = 2.0;
    public const double RecommendedModulePx = 3.0;

    private const int FrameModules = MarkerRenderer.FrameQrModuleCount;

    /// <summary>Collect frames from <paramref name="source"/> and calibrate from them.</summary>
    public static CameraRig Calibrate(ICaptureSource source, CameraCalibratorOptions options, CancellationToken cancellationToken)
    {
      var frames = CameraFrameSet.Collect(source, options.Seconds, options.MemoryBudgetBytes, options.Timeout, cancellationToken);
      return Calibrate(frames, options, cancellationToken);
    }

    /// <summary>Collect a few frames from <paramref name="source"/> and check they match <paramref name="rig"/>.</summary>
    public static IReadOnlyList<CameraCheck> Verify(
      CameraRig rig,
      ICaptureSource source,
      CameraCalibratorOptions options,
      CancellationToken cancellationToken
    )
    {
      var frames = CameraFrameSet.Collect(source, options.VerifySeconds, options.MemoryBudgetBytes, options.Timeout, cancellationToken);
      return Verify(rig, frames, options);
    }

    public static CameraRig Calibrate(CameraFrameSet frames, CameraCalibratorOptions options, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(frames);
      ArgumentNullException.ThrowIfNull(options);
      var checks = new List<CameraCheck>();
      double cameraFps = MeasureFps(frames);

      var hits = FindMarkers(frames, options.GeometryFrames, cancellationToken);
      var clusters = Cluster(hits);
      if (clusters.Count < RequiredZones)
      {
        checks.Add(
          new CameraCheck(
            "zones",
            CameraCheckLevel.Fail,
            $"Found {clusters.Count} of {RequiredZones} markers in {Math.Min(frames.Count, options.GeometryFrames)} frames. The application must "
              + "draw the marker in the TopLeft and BottomLeft slots and the camera must see both, sharp and not too small."
          )
        );
        return CreateRig(frames, options, cameraFps, Array.Empty<CameraZone>(), null, null, checks);
      }
      // The outermost two well supported markers: an application that also draws the MiddleLeft tearing marker still works, and the zones
      // stay as far apart in the scanout as possible
      int enough = Math.Max(2, clusters[0].Count / 4);
      var supported = clusters.Where(c => c.Count >= enough).OrderBy(c => c[0].Centre.Y).ToList();
      var chosen = supported.Count >= RequiredZones ? new List<List<Hit>> { supported[0], supported[^1] } : clusters.Take(RequiredZones).ToList();
      checks.Add(new CameraCheck("zones", CameraCheckLevel.Pass, $"Both markers found ({chosen[0].Count} and {chosen[1].Count} detections)."));

      var fits = chosen.Select(cluster => FitZone(frames, cluster, options.RefineFrames)).ToList();
      var timing = MeasureTiming(frames, fits.Select(f => f.Zone).ToList(), cancellationToken);

      // Scanout order: the zone the scanout reaches first times the frames
      double? delayMs = timing.DelayMs;
      if (delayMs < 0)
      {
        fits.Reverse();
        timing = timing.Swapped();
        delayMs = -delayMs;
      }
      var zones = new List<CameraZone>();
      for (int z = 0; z < fits.Count; ++z)
        zones.Add(fits[z].Zone with { ScanDelayMs = z == 0 ? 0 : delayMs ?? 0, DecodeRate = timing.DecodeRate[z] });

      AddZoneChecks(checks, fits, zones);
      AddTimingChecks(checks, frames, timing, cameraFps, delayMs);
      return CreateRig(frames, options, cameraFps, zones, delayMs, timing.RefreshHz, checks);
    }

    public static IReadOnlyList<CameraCheck> Verify(CameraRig rig, CameraFrameSet frames, CameraCalibratorOptions options)
    {
      ArgumentNullException.ThrowIfNull(rig);
      ArgumentNullException.ThrowIfNull(frames);
      var checks = new List<CameraCheck>();
      if (frames.Width != rig.CameraWidth || frames.Height != rig.CameraHeight)
      {
        checks.Add(
          new CameraCheck(
            "camera",
            CameraCheckLevel.Fail,
            $"The camera delivers {frames.Width}x{frames.Height} but the rig was calibrated at {rig.CameraWidth}x{rig.CameraHeight}; recalibrate."
          )
        );
        return checks;
      }

      double fps = MeasureFps(frames);
      if (fps > 0 && (fps < 120 || (rig.CameraFps > 0 && Math.Abs(fps - rig.CameraFps) > 0.05 * rig.CameraFps)))
      {
        checks.Add(
          new CameraCheck(
            "frame rate",
            CameraCheckLevel.Warn,
            string.Create(CultureInfo.InvariantCulture, $"The camera delivers {fps:0.#} fps; the rig was calibrated at {rig.CameraFps:0.#} fps.")
              + (fps < 120 ? " Is this a slow motion clip stored at its playback rate? Pass the recorded fps." : "")
          )
        );
      }

      // Start markers count too (an application may show one while the capture starts): they share the origin and module size
      var hits = FindMarkers(frames, Math.Min(options.GeometryFrames, 12), CancellationToken.None, includeStartMarkers: true);
      for (int z = 0; z < rig.Zones.Count; ++z)
      {
        var zone = rig.Zones[z];
        if (!zone.ModuleToCamera.TryInvert(out var cameraToModule))
          continue;
        var offsets = new List<double>();
        foreach (var hit in hits)
        {
          if (ImagePoint.Distance(hit.Geometry.TopLeft, zone.ModuleToCamera.Map(new ImagePoint(3.5, 3.5))) > 6 * zone.ModuleSizePx)
            continue;
          // The top-right finder centre sits 3.5 modules in from the right edge: that gives the symbol size (QR version) in module space
          double right = cameraToModule.Map(hit.Geometry.TopRight).X + 3.5;
          int version = Math.Clamp((int)Math.Round((right - 17) / 4), MarkerRenderer.FrameQrVersion, MarkerRenderer.MaxQrVersion);
          var expected = MarkerGeometry.ModulePoints(17 + (4 * version)).Select(p => zone.ModuleToCamera.Map(p)).ToArray();
          var found = hit.Geometry.ToArray();
          offsets.Add(Enumerable.Range(0, 4).Max(i => ImagePoint.Distance(found[i], expected[i])) / zone.ModuleSizePx);
        }
        string name = z == 0 ? "first zone" : "second zone";
        if (offsets.Count == 0)
        {
          checks.Add(
            new CameraCheck(
              name,
              CameraCheckLevel.Fail,
              "The marker was not found where the rig expects it: the camera or display moved (recalibrate), or the application only showed its "
                + "start marker."
            )
          );
          continue;
        }
        double median = Median(offsets);
        var level =
          median > 1.0 ? CameraCheckLevel.Fail
          : median > 0.5 ? CameraCheckLevel.Warn
          : CameraCheckLevel.Pass;
        checks.Add(
          new CameraCheck(
            name,
            level,
            string.Create(CultureInfo.InvariantCulture, $"Marker {median:0.00} modules from the calibrated position ({offsets.Count} detections)")
              + (level == CameraCheckLevel.Pass ? "." : ": the camera or display moved; recalibrate the rig.")
          )
        );
      }
      return checks;
    }

    /// <summary>One marker detection with all four detector points.</summary>
    private readonly record struct Hit(int FrameIndex, MarkerPayload Payload, MarkerGeometry Geometry, double ModuleSizePx)
    {
      public ImagePoint Centre =>
        new ImagePoint(
          (Geometry.TopLeft.X + Geometry.TopRight.X + Geometry.BottomLeft.X + Geometry.Alignment.X) / 4,
          (Geometry.TopLeft.Y + Geometry.TopRight.Y + Geometry.BottomLeft.Y + Geometry.Alignment.Y) / 4
        );
    }

    private sealed record ZoneFit(CameraZone Zone, double SpreadModules, double RelativeResidual, int Refined);

    private sealed record Timing(double? DelayMs, double? RefreshHz, double[] DecodeRate, double[] WhiteVariation, int DelaySamples)
    {
      public Timing Swapped() =>
        this with
        {
          DelayMs = -DelayMs,
          DecodeRate = DecodeRate.Reverse().ToArray(),
          WhiteVariation = WhiteVariation.Reverse().ToArray(),
        };
    }

    /// <summary>
    /// Full detector search (slow, so only on a spread of frames). Start markers are larger versions; calibration skips them because it fits
    /// the frame marker's geometry.
    /// </summary>
    private static List<Hit> FindMarkers(CameraFrameSet frames, int maxFrames, CancellationToken cancellationToken, bool includeStartMarkers = false)
    {
      int count = Math.Min(frames.Count, Math.Max(1, maxFrames));
      var indices = Enumerable.Range(0, count).Select(i => (int)((long)i * frames.Count / count)).Distinct().ToArray();
      var perFrame = new List<Hit>[indices.Length];
      Parallel.For(
        0,
        indices.Length,
        new ParallelOptions { CancellationToken = cancellationToken },
        () => new MarkerDecoder(tryHarder: true),
        (i, _, decoder) =>
        {
          var list = new List<Hit>();
          foreach (var result in decoder.DecodeEach(frames.Frames[indices[i]], RequiredZones + 1))
          {
            if (result.Geometry is { } geometry && (includeStartMarkers || result.Payload.Kind != MarkerKind.SequenceStart))
              list.Add(new Hit(indices[i], result.Payload, geometry, result.ModuleSizePx));
          }
          perFrame[i] = list;
          return decoder;
        },
        _ => { }
      );
      return perFrame.Where(l => l != null).SelectMany(l => l).ToList();
    }

    /// <summary>Group detections of the same marker position; the biggest groups first.</summary>
    private static List<List<Hit>> Cluster(List<Hit> hits)
    {
      var clusters = new List<List<Hit>>();
      foreach (var hit in hits)
      {
        var home = clusters.FirstOrDefault(c => ImagePoint.Distance(c[0].Centre, hit.Centre) <= 3 * Math.Max(1, c[0].ModuleSizePx));
        if (home != null)
          home.Add(hit);
        else
          clusters.Add(new List<Hit> { hit });
      }
      return clusters.OrderByDescending(c => c.Count).ToList();
    }

    private static ZoneFit FitZone(CameraFrameSet frames, List<Hit> hits, int refineFrames)
    {
      // Start from the median of every detector point, then refine against the known module pattern on several frames
      var points = new ImagePoint[4];
      for (int p = 0; p < 4; ++p)
      {
        points[p] = new ImagePoint(Median(hits.Select(h => h.Geometry.ToArray()[p].X)), Median(hits.Select(h => h.Geometry.ToArray()[p].Y)));
      }
      if (!Homography.TryFromPoints(MarkerGeometry.ModulePoints(FrameModules), points, out var initial))
        throw new InvalidOperationException("The marker detections do not define a transform");

      double spread = Spread(hits);
      var corners = new ImagePoint[] { new(0, 0), new(FrameModules, 0), new(0, FrameModules), new(FrameModules, FrameModules) };
      int step = Math.Max(1, hits.Count / Math.Max(1, refineFrames));
      var refined = new List<HomographyRefinement>();
      var chosen = hits.Where((_, i) => i % step == 0).Take(refineFrames).ToList();
      var results = new HomographyRefinement?[chosen.Count];
      Parallel.For(
        0,
        chosen.Count,
        i =>
        {
          var fit = HomographyRefiner.Refine(frames.Frames[chosen[i].FrameIndex], initial, MarkerRenderer.GenerateModules(chosen[i].Payload));
          results[i] = fit.Converged && fit.RelativeResidual < 0.25 ? fit : null;
        }
      );
      foreach (var result in results)
      {
        if (result is { } fit)
          refined.Add(fit);
      }

      var final = initial;
      double black = 0;
      double white = 255;
      double residual = double.PositiveInfinity;
      if (refined.Count > 0)
      {
        var median = new ImagePoint[4];
        for (int c = 0; c < 4; ++c)
        {
          median[c] = new ImagePoint(
            Median(refined.Select(r => r.ModuleToImage.Map(corners[c]).X)),
            Median(refined.Select(r => r.ModuleToImage.Map(corners[c]).Y))
          );
        }
        if (Homography.TryFromPoints(corners, median, out var fromCorners))
          final = fromCorners;
        black = Median(refined.Select(r => r.Black));
        white = Median(refined.Select(r => r.White));
        residual = Median(refined.Select(r => r.RelativeResidual));
      }

      var quad = corners.Select(c => final.Map(c)).ToArray();
      double shortestSide = new[]
      {
        ImagePoint.Distance(quad[0], quad[1]),
        ImagePoint.Distance(quad[0], quad[2]),
        ImagePoint.Distance(quad[1], quad[3]),
        ImagePoint.Distance(quad[2], quad[3]),
      }.Min();
      var zone = new CameraZone(final, shortestSide / FrameModules, black, white, 0, 0);
      return new ZoneFit(zone, spread / zone.ModuleSizePx, residual, refined.Count);
    }

    /// <summary>Largest standard deviation (pixels) of a detector point across the detections: camera shake or a moving marker.</summary>
    private static double Spread(List<Hit> hits)
    {
      double worst = 0;
      for (int p = 0; p < 4; ++p)
      {
        var xs = hits.Select(h => h.Geometry.ToArray()[p].X).ToArray();
        var ys = hits.Select(h => h.Geometry.ToArray()[p].Y).ToArray();
        worst = Math.Max(worst, Math.Sqrt(Variance(xs) + Variance(ys)));
      }
      return worst;
    }

    /// <summary>Decode each zone in every frame to find when the scanout reaches it, the refresh rate and the brightness stability.</summary>
    private static Timing MeasureTiming(CameraFrameSet frames, IReadOnlyList<CameraZone> zones, CancellationToken cancellationToken)
    {
      int zoneCount = zones.Count;
      var decoded = new long[zoneCount][];
      var white = new double[zoneCount][];
      var regions = zones.Select(z => z.FrameMarkerBounds(frames.Width, frames.Height, 3)).ToArray();
      var whitePoints = zones.Select(QuietZonePoints).ToArray();
      for (int z = 0; z < zoneCount; ++z)
      {
        decoded[z] = new long[frames.Count];
        white[z] = new double[frames.Count];
      }
      Parallel.For(
        0,
        frames.Count,
        new ParallelOptions { CancellationToken = cancellationToken },
        () => new MarkerDecoder(),
        (i, _, decoder) =>
        {
          var frame = frames.Frames[i];
          for (int z = 0; z < zoneCount; ++z)
          {
            var result = decoder.Decode(frame, regions[z]);
            decoded[z][i] = result.IsDecoded ? (long)result.Payload.FrameIndex : -1;
            white[z][i] = whitePoints[z].Average(p => ImageWarp.SampleBilinear(frame, p.X, p.Y));
          }
          return decoder;
        },
        _ => { }
      );

      var firstSeen = new Dictionary<long, long>[zoneCount];
      for (int z = 0; z < zoneCount; ++z)
      {
        firstSeen[z] = new Dictionary<long, long>();
        for (int i = 0; i < frames.Count; ++i)
        {
          if (decoded[z][i] >= 0)
            firstSeen[z].TryAdd(decoded[z][i], frames.Ticks[i]);
        }
      }

      // Only frames first seen after a capture where the zone showed an older frame count, not the ones already on screen when the run began
      var delays = new List<double>();
      foreach (var (frameIndex, first) in firstSeen[0])
      {
        if (zoneCount > 1 && firstSeen[1].TryGetValue(frameIndex, out long second) && first != frames.Ticks[0] && second != frames.Ticks[0])
          delays.Add((second - first) / (double)TimeSpan.TicksPerMillisecond);
      }
      double? delay = delays.Count >= 3 ? Median(delays) : null;

      var ordered = firstSeen[0].OrderBy(kv => kv.Key).ToList();
      var intervals = new List<double>();
      for (int k = 1; k < ordered.Count; ++k)
      {
        if (ordered[k].Key == ordered[k - 1].Key + 1 && ordered[k - 1].Value != frames.Ticks[0])
          intervals.Add(ordered[k].Value - ordered[k - 1].Value);
      }
      // The intervals are whole camera periods (16 or 17 ms for 60 Hz at 1000 fps): average the ones near the median instead of taking it
      double? refreshHz = null;
      if (intervals.Count >= 5)
      {
        double median = Median(intervals);
        double period = TimeSpan.TicksPerSecond / Math.Max(1, MeasureFps(frames));
        var near = intervals.Where(i => Math.Abs(i - median) <= 1.5 * period).ToList();
        refreshHz = TimeSpan.TicksPerSecond / near.Average();
      }

      var decodeRate = decoded.Select(d => d.Count(v => v >= 0) / (double)Math.Max(1, frames.Count)).ToArray();
      var whiteVariation = new double[zoneCount];
      for (int z = 0; z < zoneCount; ++z)
      {
        // Only frames where the zone decoded (so it shows a marker), and a spread that a few odd frames do not dominate
        var levels = Enumerable.Range(0, frames.Count).Where(i => decoded[z][i] >= 0).Select(i => white[z][i]).OrderBy(v => v).ToArray();
        if (levels.Length >= 10)
        {
          double p10 = levels[levels.Length / 10];
          double p90 = levels[(levels.Length * 9) / 10];
          double middle = levels[levels.Length / 2];
          whiteVariation[z] = middle > 1 ? (p90 - p10) / middle : 0;
        }
      }
      return new Timing(delay, refreshHz, decodeRate, whiteVariation, delays.Count);
    }

    /// <summary>Points in the quiet zone (always white) around the frame marker, for the brightness over time.</summary>
    private static ImagePoint[] QuietZonePoints(CameraZone zone)
    {
      var points = new List<ImagePoint>();
      for (int i = 0; i <= 8; ++i)
      {
        double t = -1.5 + ((FrameModules + 3.0) * i / 8);
        points.Add(zone.ModuleToCamera.Map(new ImagePoint(t, -1.5)));
        points.Add(zone.ModuleToCamera.Map(new ImagePoint(-1.5, t)));
      }
      return points.ToArray();
    }

    private static void AddZoneChecks(List<CameraCheck> checks, List<ZoneFit> fits, List<CameraZone> zones)
    {
      double smallest = zones.Min(z => z.ModuleSizePx);
      checks.Add(
        new CameraCheck(
          "module size",
          smallest < MinimumModulePx ? CameraCheckLevel.Fail
            : smallest < RecommendedModulePx ? CameraCheckLevel.Warn
            : CameraCheckLevel.Pass,
          string.Create(
            CultureInfo.InvariantCulture,
            $"{smallest:0.0} camera pixels per module (minimum {MinimumModulePx:0}, recommended {RecommendedModulePx:0}+)"
          ) + (smallest < RecommendedModulePx ? ": move the camera closer, zoom in or draw a larger marker." : ".")
        )
      );

      double spread = fits.Max(f => f.SpreadModules);
      checks.Add(
        new CameraCheck(
          "stability",
          spread > 0.5 ? CameraCheckLevel.Warn : CameraCheckLevel.Pass,
          string.Create(CultureInfo.InvariantCulture, $"Marker positions vary by {spread:0.00} modules between frames")
            + (spread > 0.5 ? ": the camera or display moves. Use a rigid mount." : ".")
        )
      );

      double residual = fits.Max(f => f.RelativeResidual);
      int refined = fits.Min(f => f.Refined);
      checks.Add(
        new CameraCheck(
          "focus",
          refined == 0 ? CameraCheckLevel.Fail
            : residual > 0.18 ? CameraCheckLevel.Warn
            : CameraCheckLevel.Pass,
          refined == 0
            ? "The marker model could not be fitted to the image: check focus, exposure and that the camera does not move."
            : string.Create(CultureInfo.InvariantCulture, $"Model fit residual {residual:0.00} of the contrast")
              + (residual > 0.18 ? ": the markers look blurred or noisy. Focus on the screen and avoid moiré." : ".")
        )
      );

      double black = zones.Max(z => z.Black);
      double white = zones.Min(z => z.White);
      double brightest = zones.Max(z => z.White);
      var exposure =
        white - black < 60 ? CameraCheckLevel.Warn
        : brightest >= 245 ? CameraCheckLevel.Warn
        : CameraCheckLevel.Pass;
      checks.Add(
        new CameraCheck(
          "exposure",
          exposure,
          string.Create(CultureInfo.InvariantCulture, $"Black {black:0}, white {white:0}")
            + (
              white - black < 60 ? ": too little contrast. Open the aperture, add gain or brighten the screen."
              : brightest >= 245 ? ": white clips. Shorten the exposure or close the aperture."
              : "."
            )
        )
      );
    }

    private static void AddTimingChecks(List<CameraCheck> checks, CameraFrameSet frames, Timing timing, double cameraFps, double? delayMs)
    {
      var rate =
        cameraFps < 120 ? CameraCheckLevel.Warn
        : timing.RefreshHz is { } hz && cameraFps < 2 * hz ? CameraCheckLevel.Warn
        : CameraCheckLevel.Pass;
      checks.Add(
        new CameraCheck(
          "frame rate",
          rate,
          string.Create(CultureInfo.InvariantCulture, $"Camera at {cameraFps:0.#} fps")
            + (timing.RefreshHz is { } refresh ? string.Create(CultureInfo.InvariantCulture, $", display at about {refresh:0.#} Hz") : "")
            + (
              cameraFps < 120 ? ". That is not a high speed capture: is this a slow motion clip stored at its playback rate? Pass the recorded fps."
              : rate == CameraCheckLevel.Warn ? ". Film at least twice the refresh rate."
              : "."
            )
            + (frames.DeviceTimestamps ? "" : " No device timestamps: host arrival times are used.")
        )
      );

      double worstDecode = timing.DecodeRate.Min();
      checks.Add(
        new CameraCheck(
          "decode rate",
          worstDecode < 0.5 ? CameraCheckLevel.Warn : CameraCheckLevel.Pass,
          string.Create(CultureInfo.InvariantCulture, $"Markers decoded in {worstDecode:P0} of the frames")
            + (worstDecode < 0.5 ? ": improve focus and exposure, or use a shorter exposure (less blending between frames)." : ".")
        )
      );

      double flicker = timing.WhiteVariation.Max();
      checks.Add(
        new CameraCheck(
          "flicker",
          flicker > 0.15 ? CameraCheckLevel.Warn : CameraCheckLevel.Pass,
          string.Create(CultureInfo.InvariantCulture, $"White level varies {flicker:P0} between frames (10th to 90th percentile)")
            + (flicker > 0.15 ? ": the backlight flickers (PWM dimming or strobing). Set the display to full brightness and turn off strobing." : ".")
        )
      );

      double periodMs = cameraFps > 0 ? 1000 / cameraFps : 0;
      checks.Add(
        delayMs is { } delay
          ? new CameraCheck(
            "scanout",
            delay < 0.5 * periodMs ? CameraCheckLevel.Warn : CameraCheckLevel.Pass,
            string.Create(
              CultureInfo.InvariantCulture,
              $"The scanout reaches the second marker {delay:0.00} ms after the first ({timing.DelaySamples} frames)"
            ) + (delay < 0.5 * periodMs ? ": both switch together (a strobed or global refresh display?). Camera tear detection will not work." : ".")
          )
          : new CameraCheck(
            "scanout",
            CameraCheckLevel.Warn,
            "Too few frames seen in both markers to measure the scanout. Calibrate while the application presents new frames."
          )
      );
    }

    private static CameraRig CreateRig(
      CameraFrameSet frames,
      CameraCalibratorOptions options,
      double cameraFps,
      IReadOnlyList<CameraZone> zones,
      double? delayMs,
      double? refreshHz,
      List<CameraCheck> checks
    ) =>
      new CameraRig
      {
        CreatedUtc = DateTime.UtcNow,
        Source = frames.SourceDescription,
        CameraWidth = frames.Width,
        CameraHeight = frames.Height,
        CameraFps = cameraFps,
        Mode = options.Mode,
        InputFormat = options.InputFormat,
        Zones = zones,
        ScanoutDelayMs = delayMs,
        RefreshHz = refreshHz,
        Checks = checks,
      };

    private static double MeasureFps(CameraFrameSet frames)
    {
      var deltas = new List<double>();
      for (int i = 1; i < frames.Count; ++i)
      {
        long delta = frames.Ticks[i] - frames.Ticks[i - 1];
        if (delta > 0)
          deltas.Add(delta);
      }
      return deltas.Count > 0 ? TimeSpan.TicksPerSecond / Median(deltas) : 0;
    }

    private static double Median(IEnumerable<double> values)
    {
      var sorted = values.OrderBy(v => v).ToArray();
      if (sorted.Length == 0)
        return double.NaN;
      return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static double Variance(IReadOnlyCollection<double> values)
    {
      if (values.Count < 2)
        return 0;
      double mean = values.Average();
      return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }
  }
}
