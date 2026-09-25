//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Runs one capture: source thread -> FrameRecorder -> frames.mbfc, optional start/end marker triggering, duration limit, progress reporting
//* and the capture.json sidecar. Shared by the CLI and the GUI. The start/end triggers inspect every captured frame (the recorder's
//* inspector); only the live preview image is sampled.
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
      double fps = format.FrameRate.IsKnown ? format.FrameRate.FramesPerSecond : 240;
      // The triggers look at every frame; without them the current marker for the progress display comes from the sampled preview
      // Camera captures store the timing zone's marker at a fixed place: no search (it would see two markers)
      MarkerLock? knownLock = options.Camera != null ? Camera.CameraZone.StoredLock(0) : null;
      bool camera = options.Camera != null;
      var triggers = options.WaitForStart || options.StopAtEnd ? new SequenceMonitor(knownLock, camera) : null;
      var previewMonitor = triggers == null && options.Preview != null ? new SequenceMonitor(knownLock, camera) : null;
      var recorderOptions = new FrameRecorderOptions
      {
        RingFrames = options.RingFrames ?? FrameRecorderOptions.RingFramesFor(format),
        StartArmed = options.WaitForStart,
        Inspector = triggers,
        StopAtEnd = options.StopAtEnd,
        EndTailFrames = (int)Math.Ceiling(options.EndTail.TotalSeconds * fps),
        WaitWhenFull = !source.IsLive,
        PreRollFrames = Math.Max(16, (int)Math.Ceiling(fps * 0.25)),
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

      var monitor = triggers ?? previewMonitor;
      var preview = options.Preview != null ? new GrayImage(format.Width, format.Height) : null;
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
            previewMonitor?.Inspect(preview, previewIndex);
          }
        }

        // The recorder's inspector saw the start marker (in any frame) and started writing
        if (triggers != null && recordingStartTicks < 0 && !recorder.IsArmed)
        {
          var start = triggers.Start;
          g_logger.Info("Start marker seen (run {0} '{1}'), recording", start?.Payload.RunId, start?.Start?.Name);
          recordingStartTicks = now;
        }
        // ... and the end marker plus its end tail
        if (recorder.StopRequested && stopAtTicks < 0)
        {
          g_logger.Info("End marker seen, stopping after the {0} ms end tail", options.EndTail.TotalMilliseconds);
          stopAtTicks = now;
          stopReason = "end marker";
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
      // Completing inspects the frames still in the ring; a file can end before its end marker was inspected
      recorder.Complete();
      if (recorder.StopRequested && sourceError == null)
        stopReason = "end marker";
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
        RecordedFps = options.RecordedFps,
        Camera = options.Camera,
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
