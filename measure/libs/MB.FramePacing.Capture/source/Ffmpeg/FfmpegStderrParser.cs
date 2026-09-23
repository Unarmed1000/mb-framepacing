//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Parses ffmpeg's stderr while capturing:
//*  - "Input #0 ... Stream #0:0: Video: ..., 1920x1080, 60 fps"   -> source size / rate
//*  - "Output #0 ... Stream #0:0: Video: rawvideo ..., 960x540"    -> stored frame size (how many bytes to read per frame)
//*  - "[Parsed_showinfo_N @ ..] config in time_base: 1/90000"     -> pts time base
//*  - "[Parsed_showinfo_N @ ..] n:  12 pts:  52560 ..."            -> device timestamp of output frame 12 (= capture index 12)
//*  - "... frame dropped!" / "dropping frame"                      -> frames the device/ffmpeg dropped
//* Lines arrive on the stderr reader thread; everything exposed is safe to read from other threads.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed partial class FfmpegStderrParser : IDeviceTimestampSource
  {
    private const int MaxDiagnosticLines = 40;

    private readonly ConcurrentDictionary<long, long> m_deviceTicks = new ConcurrentDictionary<long, long>();
    private readonly Queue<string> m_recentLines = new Queue<string>();
    private readonly object m_lock = new object();
    private readonly ManualResetEventSlim m_outputKnown = new ManualResetEventSlim(false);

    private Section m_section = Section.None;
    private long m_timeBaseNumerator;
    private long m_timeBaseDenominator;
    private long m_droppedFrames;
    private long m_lastFrameNumber = -1;

    private enum Section
    {
      None,
      Input,
      Output,
    }

    public VideoStreamInfo? Input { get; private set; }

    public VideoStreamInfo? Output { get; private set; }

    /// <summary>Signalled once the output stream (stored frame size) has been reported.</summary>
    public WaitHandle OutputKnown => m_outputKnown.WaitHandle;

    public long DroppedFrames => Interlocked.Read(ref m_droppedFrames);

    /// <summary>The showinfo frame number of the newest parsed frame line, -1 if none yet.</summary>
    public long LastFrameNumber => Interlocked.Read(ref m_lastFrameNumber);

    /// <summary>The last stderr lines (for error reports when ffmpeg exits unexpectedly).</summary>
    public IReadOnlyList<string> RecentLines
    {
      get
      {
        lock (m_lock)
          return m_recentLines.ToArray();
      }
    }

    public void ProcessLine(string? line)
    {
      if (string.IsNullOrEmpty(line))
        return;

      if (line.Contains("showinfo", StringComparison.Ordinal))
      {
        if (TryParseShowInfoFrame(line))
          return;
        var config = ShowInfoConfigRegex().Match(line);
        if (config.Success)
        {
          Interlocked.Exchange(ref m_timeBaseNumerator, long.Parse(config.Groups[1].Value, CultureInfo.InvariantCulture));
          Interlocked.Exchange(ref m_timeBaseDenominator, long.Parse(config.Groups[2].Value, CultureInfo.InvariantCulture));
          return;
        }
      }

      Remember(line);

      if (line.StartsWith("Input #", StringComparison.Ordinal))
        m_section = Section.Input;
      else if (line.StartsWith("Output #", StringComparison.Ordinal))
        m_section = Section.Output;
      else if (line.Contains("Stream #", StringComparison.Ordinal) && line.Contains("Video:", StringComparison.Ordinal))
        ParseStream(line);

      if (line.Contains("frame dropped", StringComparison.OrdinalIgnoreCase) || line.Contains("dropping frame", StringComparison.OrdinalIgnoreCase))
        Interlocked.Increment(ref m_droppedFrames);
    }

    /// <summary>Device timestamp (TimeSpan ticks) of output frame <paramref name="captureIndex"/>; removes it once taken.</summary>
    public bool TryGetDeviceTicks(long captureIndex, out long deviceTicks) => m_deviceTicks.TryRemove(captureIndex, out deviceTicks);

    /// <summary>Convert a pts in the given time base to TimeSpan ticks without overflow.</summary>
    public static long PtsToTicks(long pts, long timeBaseNumerator, long timeBaseDenominator)
    {
      Int128 scaled = (Int128)pts * timeBaseNumerator * TimeSpan.TicksPerSecond;
      Int128 rounded =
        scaled >= 0 ? (scaled + (timeBaseDenominator / 2)) / timeBaseDenominator : (scaled - (timeBaseDenominator / 2)) / timeBaseDenominator;
      return (long)rounded;
    }

    private bool TryParseShowInfoFrame(string line)
    {
      var frame = ShowInfoFrameRegex().Match(line);
      if (!frame.Success)
        return false;
      long number = long.Parse(frame.Groups[1].Value, CultureInfo.InvariantCulture);
      Interlocked.Exchange(ref m_lastFrameNumber, number);
      long numerator = Interlocked.Read(ref m_timeBaseNumerator);
      long denominator = Interlocked.Read(ref m_timeBaseDenominator);
      if (denominator > 0 && long.TryParse(frame.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long pts))
        m_deviceTicks[number] = PtsToTicks(pts, numerator, denominator);
      return true;
    }

    private void ParseStream(string line)
    {
      var size = StreamSizeRegex().Match(line);
      if (!size.Success)
        return;
      int width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
      int height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
      var fps = StreamFpsRegex().Match(line);
      double rate = fps.Success ? double.Parse(fps.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
      var info = new VideoStreamInfo(width, height, rate);
      if (m_section == Section.Input && Input == null)
        Input = info;
      else if (m_section == Section.Output && Output == null)
      {
        Output = info;
        m_outputKnown.Set();
      }
    }

    private void Remember(string line)
    {
      lock (m_lock)
      {
        m_recentLines.Enqueue(line);
        while (m_recentLines.Count > MaxDiagnosticLines)
          m_recentLines.Dequeue();
      }
    }

    [GeneratedRegex(@"\bn:\s*(\d+)\s+pts:\s*(-?\d+|NOPTS)")]
    private static partial Regex ShowInfoFrameRegex();

    [GeneratedRegex(@"config in time_base:\s*(\d+)/(\d+)")]
    private static partial Regex ShowInfoConfigRegex();

    // A frame size is two 2-5 digit numbers joined by 'x', not part of a hex FourCC such as 0x32595559
    [GeneratedRegex(@"(?<![0-9A-Za-z])(\d{2,5})x(\d{2,5})(?![0-9A-Za-z])")]
    private static partial Regex StreamSizeRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s+fps")]
    private static partial Regex StreamFpsRegex();
  }
}
