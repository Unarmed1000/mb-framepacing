//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds where the application draws its marker by reading frames from a source without recording them (nothing is written to disk). The
//* first step of a fast capture, which then only stores that region. A few decodes must agree: a marker that moves can not be cropped.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public static class MarkerProbe
  {
    /// <summary>Decodes that must agree on the marker position before it counts as found.</summary>
    public const int RequiredHits = 3;

    /// <summary>
    /// Read frames from <paramref name="source"/> until the marker was found <see cref="RequiredHits"/> times at the same position, the
    /// timeout passed or the source ended. Returns the frame marker lock in the source's frame coordinates.
    /// </summary>
    /// <param name="decodeInterval">Minimum host time between two decodes; the frames in between are only read. Zero decodes every frame
    /// (for sources that wait, like files).</param>
    public static MarkerLock Locate(ICaptureSource source, TimeSpan timeout, TimeSpan decodeInterval, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(source);
      using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      stop.CancelAfter(timeout);
      var sink = new Sink(source.Format.Width, source.Format.Height, decodeInterval.Ticks, stop);
      source.Run(sink, new CaptureClock(), stop.Token);
      cancellationToken.ThrowIfCancellationRequested();

      if (sink.Moved is { } moved)
        throw new InvalidOperationException(
          $"The marker moved between frames ({sink.Hits[0].Bounds} and {moved.Bounds}). "
            + "Only storing the marker region needs the marker at a fixed position; capture the whole frame instead."
        );
      if (sink.Hits.Count == 0)
        throw new TimeoutException(
          $"No marker was found in {sink.FramesSeen} frames ({timeout.TotalSeconds:0.#} s). Check that the application is running and draws the "
            + "marker (see doc/marker-format.md 'Sizing')."
        );
      return new MarkerLock(
        new PixelRect(
          Median(sink.Hits.Select(h => h.Bounds.X)),
          Median(sink.Hits.Select(h => h.Bounds.Y)),
          Median(sink.Hits.Select(h => h.Bounds.Width)),
          Median(sink.Hits.Select(h => h.Bounds.Height))
        ),
        sink.Hits.Select(h => h.ModuleSizePx).OrderBy(m => m).ElementAt(sink.Hits.Count / 2)
      );
    }

    private static int Median(IEnumerable<int> values)
    {
      var sorted = values.OrderBy(v => v).ToArray();
      return sorted[sorted.Length / 2];
    }

    /// <summary>Two locks describe the same marker position when origin and module size agree within about a module.</summary>
    private static bool Agrees(MarkerLock first, MarkerLock other)
    {
      float tolerance = Math.Max(2f, first.ModuleSizePx);
      return Math.Abs(first.Bounds.X - other.Bounds.X) <= tolerance
        && Math.Abs(first.Bounds.Y - other.Bounds.Y) <= tolerance
        && Math.Abs(first.ModuleSizePx - other.ModuleSizePx) <= 0.25f * first.ModuleSizePx;
    }

    private sealed class Sink : IFrameSink
    {
      private readonly GrayImage m_frame;
      private readonly MarkerDecoder m_decoder = new MarkerDecoder(tryHarder: true);
      private readonly long m_intervalTicks;
      private readonly CancellationTokenSource m_stop;
      private long m_lastDecodeTicks = long.MinValue;

      public Sink(int width, int height, long intervalTicks, CancellationTokenSource stop)
      {
        m_frame = new GrayImage(width, height);
        m_intervalTicks = intervalTicks;
        m_stop = stop;
      }

      public List<MarkerLock> Hits { get; } = new List<MarkerLock>();

      public MarkerLock? Moved { get; private set; }

      public long FramesSeen { get; private set; }

      public Span<byte> BeginFrame() => m_frame.Pixels.AsSpan(0, m_frame.Width * m_frame.Height);

      public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags)
      {
        ++FramesSeen;
        if (m_stop.IsCancellationRequested || (m_lastDecodeTicks != long.MinValue && hostTicks - m_lastDecodeTicks < m_intervalTicks))
          return;
        m_lastDecodeTicks = hostTicks;

        var result = m_decoder.Decode(m_frame);
        if (!result.IsDecoded || result.ModuleSizePx <= 0)
          return;
        var hit = SequenceMonitor.LockFor(result);
        if (Hits.Count > 0 && !Agrees(Hits[0], hit))
        {
          Moved = hit;
          m_stop.Cancel();
          return;
        }
        Hits.Add(hit);
        if (Hits.Count >= RequiredHits)
          m_stop.Cancel();
      }
    }
  }
}
