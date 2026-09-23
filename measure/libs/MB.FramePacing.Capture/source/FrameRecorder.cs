//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The capture hot path. A single producer (the capture source) / single consumer (the writer thread) ring of fixed size record slots in one
//* pinned array. The source reads pixels straight into a slot, so a frame is copied exactly once (device -> ring) before the writer hands
//* contiguous runs of slots to the file in large sequential writes.
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
  public sealed class FrameRecorder : IFrameSink, IDisposable
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private readonly CaptureFileWriter m_writer;
    private readonly FrameRecorderOptions m_options;
    private readonly IDeviceTimestampSource? m_timestamps;
    private readonly CaptureClock m_clock;
    private readonly int m_recordSize;
    private readonly int m_pixelByteCount;
    private readonly int m_slotCount;
    private readonly byte[] m_ring;
    private readonly byte[] m_scratch;
    private readonly AutoResetEvent m_dataAvailable = new AutoResetEvent(false);
    private readonly Thread m_writerThread;
    private readonly object m_previewLock = new object();
    private readonly byte[]? m_preview;

    // m_head is written by the producer only, m_tail by the writer only; both are read across threads with Volatile.
    private long m_head;
    private long m_tail;
    private long m_nextCaptureIndex;
    private bool m_currentFrameDropped;
    private long m_framesDropped;
    private long m_framesDiscarded;
    private volatile bool m_armed;
    private volatile bool m_completing;
    private volatile Exception? m_writerError;
    private long m_lastPreviewTicks = long.MinValue;
    private long m_previewCaptureIndex = -1;
    private bool m_disposed;

    public FrameRecorder(CaptureFileWriter writer, FrameRecorderOptions options, CaptureClock clock, IDeviceTimestampSource? deviceTimestamps = null)
    {
      m_writer = writer ?? throw new ArgumentNullException(nameof(writer));
      m_options = options ?? throw new ArgumentNullException(nameof(options));
      m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
      m_timestamps = deviceTimestamps;
      m_recordSize = writer.Header.RecordSize;
      m_pixelByteCount = writer.Header.PixelByteCount;
      m_slotCount = Math.Max(options.RingFrames, 2);
      if ((long)m_slotCount * m_recordSize > Array.MaxLength)
        throw new ArgumentException($"A ring of {m_slotCount} frames of {m_recordSize} bytes is too large", nameof(options));

      m_ring = GC.AllocateUninitializedArray<byte>(m_slotCount * m_recordSize, pinned: true);
      m_scratch = GC.AllocateUninitializedArray<byte>(m_pixelByteCount, pinned: true);
      m_preview = options.PreviewInterval > TimeSpan.Zero ? new byte[m_pixelByteCount] : null;
      m_armed = options.StartArmed;
      m_writerThread = new Thread(WriterLoop)
      {
        Name = "FrameRecorder.Writer",
        IsBackground = true,
        Priority = ThreadPriority.AboveNormal,
      };
      m_writerThread.Start();
    }

    public int Width => m_writer.Header.Width;
    public int Height => m_writer.Header.Height;

    public bool IsArmed => m_armed;

    public FrameRecorderStats Stats
    {
      get
      {
        long head = Volatile.Read(ref m_head);
        long tail = Volatile.Read(ref m_tail);
        return new FrameRecorderStats(
          Interlocked.Read(ref m_nextCaptureIndex),
          m_writer.RecordsWritten,
          Interlocked.Read(ref m_framesDropped),
          Interlocked.Read(ref m_framesDiscarded),
          m_writer.BytesWritten,
          (int)(head - tail),
          m_slotCount,
          m_armed
        );
      }
    }

    /// <summary>Leave armed mode: the pre-roll frames and everything after them are written.</summary>
    public void StartWriting()
    {
      if (m_armed)
      {
        m_armed = false;
        m_dataAvailable.Set();
      }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Producer side (capture source thread)
    // ------------------------------------------------------------------------------------------------------------------------------------------

    public Span<byte> BeginFrame()
    {
      long head = m_head;
      long tail = Volatile.Read(ref m_tail);
      if (m_options.WaitWhenFull && !m_armed)
      {
        var spin = new SpinWait();
        while (head - tail >= m_slotCount && m_writerError == null && !m_completing)
        {
          spin.SpinOnce(sleep1Threshold: 20);
          tail = Volatile.Read(ref m_tail);
        }
      }
      if (head - tail >= m_slotCount || m_writerError != null || m_completing)
      {
        m_currentFrameDropped = true;
        return m_scratch.AsSpan(0, m_pixelByteCount);
      }
      m_currentFrameDropped = false;
      return m_ring.AsSpan(SlotOffset(head) + CaptureFileHeader.RecordHeaderSize, m_pixelByteCount);
    }

    public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags)
    {
      long captureIndex = m_nextCaptureIndex;
      Volatile.Write(ref m_nextCaptureIndex, captureIndex + 1);

      Span<byte> pixels;
      if (m_currentFrameDropped)
      {
        Interlocked.Increment(ref m_framesDropped);
        if (g_logger.IsTraceEnabled)
          g_logger.Trace("Ring full, dropped capture index {0}", captureIndex);
        pixels = m_scratch;
      }
      else
      {
        long head = m_head;
        int offset = SlotOffset(head);
        var slot = m_ring.AsSpan(offset, m_recordSize);
        new CaptureRecordHeader(captureIndex, hostTicks, deviceTicks, flags, m_pixelByteCount).Write(slot);
        slot.Slice(CaptureFileHeader.RecordHeaderSize + m_pixelByteCount).Clear();
        pixels = slot.Slice(CaptureFileHeader.RecordHeaderSize, m_pixelByteCount);
        Volatile.Write(ref m_head, head + 1);
        m_dataAvailable.Set();
      }
      UpdatePreview(pixels.Slice(0, m_pixelByteCount), captureIndex, hostTicks);
    }

    /// <summary>Copy the newest preview frame (taken every <see cref="FrameRecorderOptions.PreviewInterval"/>). Returns its capture index or -1.</summary>
    public long TryCopyPreview(GrayImage target)
    {
      if (m_preview == null)
        return -1;
      if (target.Width != Width || target.Height != Height || target.Stride != Width)
        throw new ArgumentException("Preview target must match the capture frame size", nameof(target));
      lock (m_previewLock)
      {
        if (m_previewCaptureIndex < 0)
          return -1;
        m_preview.CopyTo(target.Pixels, 0);
        return m_previewCaptureIndex;
      }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Completion
    // ------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Stop accepting frames, write everything still in the ring (unless armed) and wait for the writer. Rethrows writer errors.</summary>
    public void Complete()
    {
      m_completing = true;
      m_dataAvailable.Set();
      m_writerThread.Join();
      if (m_writerError != null)
        throw new IOException("Writing the capture file failed: " + m_writerError.Message, m_writerError);
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      if (m_writerThread.IsAlive)
      {
        m_completing = true;
        m_dataAvailable.Set();
        m_writerThread.Join();
      }
      m_dataAvailable.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Writer thread
    // ------------------------------------------------------------------------------------------------------------------------------------------

    private void WriterLoop()
    {
      try
      {
        long waitTicks = m_options.DeviceTicksWait.Ticks;
        while (true)
        {
          long head = Volatile.Read(ref m_head);
          long tail = m_tail;
          bool completing = m_completing;

          if (m_armed)
          {
            long keep = Math.Clamp(m_options.PreRollFrames, 0, m_slotCount - 1);
            if (head - tail > keep)
            {
              Interlocked.Add(ref m_framesDiscarded, head - keep - tail);
              Volatile.Write(ref m_tail, head - keep);
            }
            if (completing)
              break;
            m_dataAvailable.WaitOne(20);
            continue;
          }

          if (head == tail)
          {
            if (completing)
              break;
            m_dataAvailable.WaitOne(20);
            continue;
          }

          int count = (int)Math.Min(Math.Min(head - tail, m_slotCount - (tail % m_slotCount)), m_options.MaxBatchRecords);
          bool ringPressure = (head - tail) * 2 > m_slotCount;
          int ready = ResolveDeviceTicks(tail, count, completing || ringPressure, waitTicks);
          if (ready == 0)
          {
            Thread.Sleep(1);
            continue;
          }

          m_writer.WriteRecords(m_ring.AsSpan(SlotOffset(tail), ready * m_recordSize));
          Volatile.Write(ref m_tail, tail + ready);
        }
      }
      catch (Exception ex)
      {
        g_logger.Error(ex, "Capture writer failed");
        m_writerError = ex;
      }
    }

    /// <summary>Fill in pending device timestamps. Returns how many records starting at <paramref name="first"/> are ready to write.</summary>
    private int ResolveDeviceTicks(long first, int count, bool force, long waitTicks)
    {
      for (int i = 0; i < count; ++i)
      {
        var slot = m_ring.AsSpan(SlotOffset(first + i), CaptureFileHeader.RecordHeaderSize);
        var header = CaptureRecordHeader.Read(slot);
        if (header.DeviceTicks != DeviceTimestamps.PendingTicks)
          continue;

        if (m_timestamps != null && m_timestamps.TryGetDeviceTicks(header.CaptureIndex, out long deviceTicks))
        {
          (header with { DeviceTicks = deviceTicks }).Write(slot);
          continue;
        }
        if (!force && m_clock.NowTicks - header.HostTicks < waitTicks)
          return i;
        (header with { DeviceTicks = CaptureRecordHeader.UnknownTicks }).Write(slot);
      }
      return count;
    }

    private int SlotOffset(long sequence) => (int)(sequence % m_slotCount) * m_recordSize;

    private void UpdatePreview(ReadOnlySpan<byte> pixels, long captureIndex, long hostTicks)
    {
      if (m_preview == null)
        return;
      if (m_lastPreviewTicks != long.MinValue && hostTicks - m_lastPreviewTicks < m_options.PreviewInterval.Ticks)
        return;
      m_lastPreviewTicks = hostTicks;
      lock (m_previewLock)
      {
        pixels.CopyTo(m_preview);
        m_previewCaptureIndex = captureIndex;
      }
    }
  }
}
