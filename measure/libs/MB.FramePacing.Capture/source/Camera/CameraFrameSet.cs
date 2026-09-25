//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A short run of whole camera frames held in memory for calibrating or verifying a camera rig (EXPERIMENTAL camera support). Collecting
//* first and processing afterwards works the same for live devices (which can not wait) and clips.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Camera
{
  /// <summary>A short run of whole camera frames held in memory for calibrating or verifying a camera rig.</summary>
  public sealed class CameraFrameSet
  {
    public CameraFrameSet(IReadOnlyList<GrayImage> frames, IReadOnlyList<long> ticks, bool deviceTimestamps, string sourceDescription)
    {
      if (frames.Count != ticks.Count)
        throw new ArgumentException("Every frame needs a timestamp");
      Frames = frames;
      Ticks = ticks;
      DeviceTimestamps = deviceTimestamps;
      SourceDescription = sourceDescription;
    }

    public IReadOnlyList<GrayImage> Frames { get; }

    /// <summary>Capture time of every frame (device ticks when all frames had them, otherwise host ticks).</summary>
    public IReadOnlyList<long> Ticks { get; }

    public bool DeviceTimestamps { get; }

    public string SourceDescription { get; }

    public int Count => Frames.Count;

    public int Width => Frames.Count > 0 ? Frames[0].Width : 0;

    public int Height => Frames.Count > 0 ? Frames[0].Height : 0;

    /// <summary>
    /// Read up to <paramref name="seconds"/> of frames (at the source's nominal rate) from <paramref name="source"/>, at most
    /// <paramref name="memoryBudgetBytes"/> of pixels, giving up after <paramref name="timeout"/>.
    /// </summary>
    public static CameraFrameSet Collect(
      ICaptureSource source,
      double seconds,
      long memoryBudgetBytes,
      TimeSpan timeout,
      CancellationToken cancellationToken
    )
    {
      ArgumentNullException.ThrowIfNull(source);
      double fps = source.Format.FrameRate.IsKnown ? source.Format.FrameRate.FramesPerSecond : 240;
      long frameBytes = (long)source.Format.Width * source.Format.Height;
      int maxFrames = (int)Math.Clamp(Math.Min(Math.Ceiling(seconds * fps), memoryBudgetBytes / Math.Max(1, frameBytes)), 1, int.MaxValue);

      using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      stop.CancelAfter(timeout);
      var sink = new Sink(source.Format.Width, source.Format.Height, maxFrames, stop);
      source.Run(sink, new CaptureClock(), stop.Token);
      cancellationToken.ThrowIfCancellationRequested();
      if (sink.Frames.Count == 0)
        throw new TimeoutException($"The source delivered no frames within {timeout.TotalSeconds:0.#} s");

      // Device timestamps may arrive after the pixels (ffmpeg's showinfo on stderr): give them a moment
      bool device = ResolveDeviceTicks(source, sink);
      var ticks = new long[sink.Frames.Count];
      for (int i = 0; i < ticks.Length; ++i)
        ticks[i] = device ? sink.DeviceTicks[i] : sink.HostTicks[i];
      return new CameraFrameSet(sink.Frames, ticks, device, source.Description);
    }

    private static bool ResolveDeviceTicks(ICaptureSource source, Sink sink)
    {
      var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
      while (true)
      {
        bool complete = true;
        for (int i = 0; i < sink.DeviceTicks.Count; ++i)
        {
          if (sink.DeviceTicks[i] != Capture.DeviceTimestamps.PendingTicks)
            continue;
          if (source.DeviceTimestamps != null && source.DeviceTimestamps.TryGetDeviceTicks(i, out long ticks))
            sink.DeviceTicks[i] = ticks;
          else
            complete = false;
        }
        if (complete || DateTime.UtcNow > deadline)
          break;
        Thread.Sleep(20);
      }
      foreach (long ticks in sink.DeviceTicks)
      {
        if (ticks == Capture.DeviceTimestamps.PendingTicks || ticks == CaptureRecordHeader.UnknownTicks)
          return false;
      }
      return true;
    }

    private sealed class Sink : IFrameSink
    {
      private readonly int m_width;
      private readonly int m_height;
      private readonly int m_maxFrames;
      private readonly CancellationTokenSource m_stop;
      private GrayImage? m_current;

      public Sink(int width, int height, int maxFrames, CancellationTokenSource stop)
      {
        m_width = width;
        m_height = height;
        m_maxFrames = maxFrames;
        m_stop = stop;
      }

      public List<GrayImage> Frames { get; } = new List<GrayImage>();
      public List<long> HostTicks { get; } = new List<long>();
      public List<long> DeviceTicks { get; } = new List<long>();

      public Span<byte> BeginFrame()
      {
        m_current = new GrayImage(m_width, m_height);
        return m_current.Pixels.AsSpan(0, m_width * m_height);
      }

      public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags)
      {
        if (m_current == null || Frames.Count >= m_maxFrames)
          return;
        Frames.Add(m_current);
        HostTicks.Add(hostTicks);
        DeviceTicks.Add(deviceTicks);
        m_current = null;
        if (Frames.Count >= m_maxFrames)
          m_stop.Cancel();
      }
    }
  }
}
