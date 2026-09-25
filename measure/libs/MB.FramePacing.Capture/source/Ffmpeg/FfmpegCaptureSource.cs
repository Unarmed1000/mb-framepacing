//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture through an ffmpeg process: raw Gray8 frames on stdout go straight into the recorder's ring, device timestamps arrive on stderr
//* (showinfo) and are resolved by the recorder's writer thread. ffmpeg itself reports the stored frame size, so a capture works even when
//* neither mode nor scale is given.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed class FfmpegCaptureSource : ICaptureSource
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private readonly Process m_process;
    private readonly FfmpegStderrParser m_parser = new FfmpegStderrParser();
    private readonly Thread m_stderrThread;
    private int m_stopRequested;
    private bool m_disposed;

    private FfmpegCaptureSource(FfmpegCaptureOptions options)
    {
      Options = options;
      var startInfo = new ProcessStartInfo(options.FfmpegPath)
      {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      foreach (var argument in FfmpegCommandBuilder.BuildCapture(options))
        startInfo.ArgumentList.Add(argument);
      g_logger.Info("Starting ffmpeg: {0} {1}", options.FfmpegPath, string.Join(" ", startInfo.ArgumentList.Select(Quote)));

      m_process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg");
      m_stderrThread = new Thread(ReadStderr) { Name = "ffmpeg.stderr", IsBackground = true };
      m_stderrThread.Start();
      Format = null!;
    }

    public FfmpegCaptureOptions Options { get; }

    public string Description => $"ffmpeg {Options.Device.Kind} '{Options.Device.Name}'";

    public CaptureFormat Format { get; private set; }

    public IDeviceTimestampSource? DeviceTimestamps => m_parser;

    public long SourceDroppedFrames => m_parser.DroppedFrames;

    public bool IsLive => Options.Device.IsLive;

    /// <summary>ffmpeg's last stderr lines, for diagnostics.</summary>
    public string RecentLog => string.Join(Environment.NewLine, m_parser.RecentLines);

    /// <summary>Start ffmpeg and wait until it reports the output frame size.</summary>
    public static FfmpegCaptureSource Start(FfmpegCaptureOptions options, TimeSpan startupTimeout)
    {
      var source = new FfmpegCaptureSource(options);
      try
      {
        var deadline = Stopwatch.StartNew();
        while (!source.m_parser.OutputKnown.WaitOne(50))
        {
          if (source.m_process.HasExited)
            throw new InvalidOperationException(
              $"ffmpeg exited with code {source.m_process.ExitCode} before capturing:{Environment.NewLine}{source.RecentLog}"
            );
          if (deadline.Elapsed > startupTimeout)
            throw new TimeoutException(
              $"ffmpeg did not start capturing within {startupTimeout.TotalSeconds:0}s:{Environment.NewLine}{source.RecentLog}"
            );
        }

        var output = source.m_parser.Output!.Value;
        var input = source.m_parser.Input;
        double fps =
          options.RecordedFps is > 0 ? options.RecordedFps.Value
          : options.Mode.HasFps ? options.Mode.Fps
          : input?.Fps ?? 0;
        source.Format = new CaptureFormat(
          output.Width,
          output.Height,
          FrameRate.FromFps(fps),
          input?.Width ?? 0,
          input?.Height ?? 0,
          options.Roi ?? default
        );
        g_logger.Info(
          "ffmpeg capturing: source {0}x{1}@{2:0.###}, stored {3}x{4}",
          source.Format.SourceWidth,
          source.Format.SourceHeight,
          fps,
          source.Format.Width,
          source.Format.Height
        );
        return source;
      }
      catch
      {
        source.Dispose();
        throw;
      }
    }

    public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken)
    {
      var stdout = m_process.StandardOutput.BaseStream;
      using var registration = cancellationToken.Register(RequestStop);
      long reportedDrops = 0;
      var knownTimestamps = Options.FrameTimestamps;
      int frameNumber = 0;

      while (true)
      {
        if (knownTimestamps != null && frameNumber >= knownTimestamps.Count)
        {
          // Every listed frame arrived (ffmpeg repeats the last image of a concat list); stop here
          RequestStop();
          break;
        }
        var buffer = sink.BeginFrame();
        if (!ReadFrame(stdout, buffer))
          break;
        long hostTicks = clock.NowTicks;

        var flags = CaptureRecordFlags.None;
        long drops = m_parser.DroppedFrames;
        if (drops != reportedDrops)
        {
          reportedDrops = drops;
          flags |= CaptureRecordFlags.SourceDropBefore;
        }
        long deviceTicks =
          knownTimestamps != null ? knownTimestamps[frameNumber]
          : Options.RecordedFps is > 0 ? (long)Math.Round(frameNumber * (double)TimeSpan.TicksPerSecond / Options.RecordedFps.Value)
          : DeviceTimestampsPending;
        ++frameNumber;
        sink.EndFrame(hostTicks, deviceTicks, flags);
      }

      if (Volatile.Read(ref m_stopRequested) == 0)
      {
        m_process.WaitForExit(2000);
        if (m_process.HasExited && m_process.ExitCode != 0)
          throw new InvalidOperationException($"ffmpeg stopped unexpectedly (exit code {m_process.ExitCode}):{Environment.NewLine}{RecentLog}");
      }
    }

    /// <summary>Ask ffmpeg to finish (the 'q' command), which flushes and closes stdout.</summary>
    public void RequestStop()
    {
      if (Interlocked.Exchange(ref m_stopRequested, 1) != 0)
        return;
      try
      {
        if (!m_process.HasExited)
        {
          m_process.StandardInput.Write('q');
          m_process.StandardInput.Flush();
        }
      }
      catch (IOException) { }
      catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      RequestStop();
      try
      {
        if (!m_process.WaitForExit(5000))
        {
          g_logger.Warn("ffmpeg did not exit, killing it");
          m_process.Kill(entireProcessTree: true);
          m_process.WaitForExit(2000);
        }
      }
      catch (InvalidOperationException) { }
      m_stderrThread.Join(2000);
      m_process.Dispose();
    }

    private const long DeviceTimestampsPending = Capture.DeviceTimestamps.PendingTicks;

    private static bool ReadFrame(Stream stream, Span<byte> buffer)
    {
      int total = 0;
      while (total < buffer.Length)
      {
        int read = stream.Read(buffer.Slice(total));
        if (read <= 0)
        {
          if (total > 0)
            g_logger.Warn("ffmpeg output ended in the middle of a frame ({0} of {1} bytes)", total, buffer.Length);
          return false;
        }
        total += read;
      }
      return true;
    }

    private void ReadStderr()
    {
      try
      {
        string? line;
        while ((line = m_process.StandardError.ReadLine()) != null)
        {
          m_parser.ProcessLine(line);
          if (g_logger.IsTraceEnabled && !line.Contains("showinfo", StringComparison.Ordinal))
            g_logger.Trace("ffmpeg: {0}", line);
        }
      }
      catch (IOException) { }
      catch (ObjectDisposedException) { }
    }

    private static string Quote(string argument) => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
  }
}
