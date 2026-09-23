//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Runs one capture: source thread -> FrameRecorder -> frames.mbfc, optional start/end marker triggering, duration limit, progress reporting
//* and the capture.json sidecar. Shared by the CLI and the GUI.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Threading;
using MB.FramePacing.Marker;
using NLog;

namespace MB.FramePacing.Capture
{
  public sealed record CaptureRunOptions
  {
    public required string OutputDirectory { get; init; }

    /// <summary>Stop after this long (measured from the first written frame when waiting for a start marker). Null = until cancelled.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Hold frames back until a start marker is seen (a short pre-roll is kept).</summary>
    public bool WaitForStart { get; init; }

    /// <summary>Stop once the end marker of the run has been seen (plus <see cref="EndTail"/>).</summary>
    public bool StopAtEnd { get; init; }

    public TimeSpan EndTail { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Ring size in frames; null sizes it for one second of frames (at most 512 MiB).</summary>
    public int? RingFrames { get; init; }

    /// <summary>
    /// Receives a copy of the newest frame roughly every <see cref="FrameRecorderOptions.PreviewInterval"/> (on the runner's thread; the image
    /// is reused, copy what you need before returning). Null = no preview.
    /// </summary>
    public Action<GrayImage, long>? Preview { get; init; }

    public string ToolVersion { get; init; } = string.Empty;
    public string? FfmpegVersion { get; init; }
    public string? FfmpegCommandLine { get; init; }
  }

  public enum CapturePhase
  {
    WaitingForStart,
    Recording,
    Stopping,
    Finished,
  }

  public readonly record struct CaptureProgress(
    CapturePhase Phase,
    TimeSpan Elapsed,
    FrameRecorderStats Recorder,
    long SourceDroppedFrames,
    MarkerDecodeResult? LastMarker
  )
  {
    public double CapturedFps(TimeSpan window) => window > TimeSpan.Zero ? Recorder.FramesCaptured / window.TotalSeconds : 0;
  }

  public sealed record CaptureResult(string Directory, string FramesPath, CaptureSessionInfo Session);

  public static class CaptureRunner
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    public static CaptureResult Run(
      ICaptureSource source,
      CaptureRunOptions options,
      Action<CaptureProgress>? progress,
      CancellationToken cancellationToken
    )
    {
      Directory.CreateDirectory(options.OutputDirectory);
      var framesPath = Path.Combine(options.OutputDirectory, CaptureSessionInfo.FramesFileName);
      if (File.Exists(framesPath))
        throw new IOException($"'{framesPath}' already exists; choose a new output directory");

      var format = source.Format;
      var header = format.ToFileHeader();
      long expectedRecords =
        options.Duration is { } duration && format.FrameRate.IsKnown && !options.WaitForStart
          ? (long)Math.Ceiling(duration.TotalSeconds * format.FrameRate.FramesPerSecond * 1.05) + 16
          : 0;
      var recorderOptions = new FrameRecorderOptions
      {
        RingFrames = options.RingFrames ?? FrameRecorderOptions.RingFramesFor(format),
        StartArmed = options.WaitForStart,
        WaitWhenFull = !source.IsLive,
        PreRollFrames = Math.Max(16, (int)Math.Ceiling((format.FrameRate.IsKnown ? format.FrameRate.FramesPerSecond : 240) * 0.25)),
      };

      var clock = new CaptureClock();
      var startedUtc = DateTime.UtcNow;
      using var writer = new CaptureFileWriter(framesPath, header, expectedRecords);
      using var recorder = new FrameRecorder(writer, recorderOptions, clock, source.DeviceTimestamps);
      using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

      Exception? sourceError = null;
      var sourceThread = new Thread(() =>
      {
        try
        {
          source.Run(recorder, clock, stopSource.Token);
        }
        catch (Exception ex)
        {
          sourceError = ex;
        }
      })
      {
        Name = "CaptureSource",
        IsBackground = true,
        Priority = ThreadPriority.Highest,
      };

      g_logger.Info(
        "Capturing {0} -> {1} ({2}x{3}, ring {4} frames)",
        source.Description,
        framesPath,
        format.Width,
        format.Height,
        recorderOptions.RingFrames
      );
      sourceThread.Start();

      // The monitor decodes preview frames: needed for the start/end triggers, and it reports the current marker to a live view
      var monitor = options.WaitForStart || options.StopAtEnd || options.Preview != null ? new SequenceMonitor() : null;
      var preview = monitor != null || options.Preview != null ? new GrayImage(format.Width, format.Height) : null;
      long lastPreviewIndex = -1;
      long recordingStartTicks = options.WaitForStart ? -1 : 0;
      long stopAtTicks = -1;
      string stopReason = "cancelled";
      long lastReportTicks = 0;

      while (sourceThread.IsAlive)
      {
        sourceThread.Join(20);
        long now = clock.NowTicks;

        if (preview != null)
        {
          long previewIndex = recorder.TryCopyPreview(preview);
          if (previewIndex >= 0 && previewIndex != lastPreviewIndex)
          {
            lastPreviewIndex = previewIndex;
            options.Preview?.Invoke(preview, previewIndex);
          }
          if (monitor != null && previewIndex >= 0 && previewIndex == lastPreviewIndex && monitor.LastInspectedIndex != previewIndex)
          {
            monitor.LastInspectedIndex = previewIndex;
            monitor.Inspect(preview);
            if (recorder.IsArmed && monitor.Start != null)
            {
              g_logger.Info("Start marker seen (run {0} '{1}'), recording", monitor.Start.Value.Payload.RunId, monitor.Start.Value.Start?.Name);
              recorder.StartWriting();
              recordingStartTicks = now;
            }
            if (options.StopAtEnd && monitor.EndSeen && stopAtTicks < 0)
            {
              g_logger.Info("End marker seen, stopping after {0} ms", options.EndTail.TotalMilliseconds);
              stopAtTicks = now + options.EndTail.Ticks;
              stopReason = "end marker";
            }
          }
        }

        if (recordingStartTicks >= 0 && options.Duration is { } limit && stopAtTicks < 0 && now - recordingStartTicks >= limit.Ticks)
        {
          stopAtTicks = now;
          stopReason = "duration";
        }
        if (stopAtTicks >= 0 && now >= stopAtTicks && !stopSource.IsCancellationRequested)
          stopSource.Cancel();

        if (progress != null && now - lastReportTicks >= TimeSpan.TicksPerMillisecond * 200)
        {
          lastReportTicks = now;
          var phase =
            stopSource.IsCancellationRequested ? CapturePhase.Stopping
            : recorder.IsArmed ? CapturePhase.WaitingForStart
            : CapturePhase.Recording;
          progress(new CaptureProgress(phase, TimeSpan.FromTicks(now), recorder.Stats, source.SourceDroppedFrames, monitor?.Last));
        }
      }

      if (!stopSource.IsCancellationRequested)
        stopReason = sourceError != null ? "source error" : "source ended";
      recorder.Complete();
      var stats = recorder.Stats;
      var session = new CaptureSessionInfo
      {
        ToolVersion = options.ToolVersion,
        StartedUtc = startedUtc,
        Source = source.Description,
        FfmpegVersion = options.FfmpegVersion,
        FfmpegCommandLine = options.FfmpegCommandLine,
        Width = format.Width,
        Height = format.Height,
        SourceWidth = format.SourceWidth,
        SourceHeight = format.SourceHeight,
        Roi = format.Roi.IsEmpty ? null : format.Roi.ToString(),
        NominalFps = format.FrameRate.FramesPerSecond,
        WaitedForStart = options.WaitForStart,
        StopAtEnd = options.StopAtEnd,
        DurationSeconds = TimeSpan.FromTicks(clock.NowTicks).TotalSeconds,
        FramesCaptured = stats.FramesCaptured,
        FramesWritten = stats.FramesWritten,
        FramesDroppedByRecorder = stats.FramesDropped,
        FramesDroppedBySource = source.SourceDroppedFrames,
        FramesDiscardedBeforeStart = stats.FramesDiscardedWhileArmed,
        StopReason = stopReason,
        SequenceRunId = monitor?.Start?.Payload.RunId,
        SequenceName = monitor?.Start?.Start?.Name,
      };
      session.Save(options.OutputDirectory);
      progress?.Invoke(
        new CaptureProgress(CapturePhase.Finished, TimeSpan.FromTicks(clock.NowTicks), stats, source.SourceDroppedFrames, monitor?.Last)
      );

      if (sourceError != null)
        throw new InvalidOperationException("Capture source failed: " + sourceError.Message, sourceError);
      return new CaptureResult(options.OutputDirectory, framesPath, session);
    }
  }
}
