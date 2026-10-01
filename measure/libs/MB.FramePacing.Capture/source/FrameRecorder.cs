//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The capture hot path. A single producer (the capture source) ring of fixed size record slots in one pinned array. The source reads pixels
//* straight into a slot, so a frame is copied exactly once (device -> ring) before the writer hands contiguous runs of slots to the files in
//* large sequential writes.
//*
//* With a decoder or an inspector a second consumer sits between the two: the inspection thread looks at every frame in order, and the
//* writer only writes or discards frames that were inspected. Nothing is sampled, so a marker in a single frame is enough. The decoder
//* reads every frame's markers into a second ring of captures.mbcd records (the capture data); the inspector (the start/end marker
//* triggers) gets that decode. The writer writes the data records, and the frames themselves only when they are kept (frames.mbfc).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Threading;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;
using NLog;

namespace MB.FramePacing.Capture
{
  public sealed class FrameRecorder : IFrameSink, IDisposable
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private readonly CaptureFileHeader m_header;
    private readonly CaptureFileWriter? m_frames;
    private readonly CaptureDataWriter? m_data;
    private readonly FrameRecorderOptions m_options;
    private readonly IDeviceTimestampSource? m_timestamps;
    private readonly CaptureClock m_clock;
    private readonly int m_recordSize;
    private readonly int m_pixelByteCount;
    private readonly int m_slotCount;
    private readonly byte[] m_ring;
    private readonly byte[]? m_dataRing;
    private readonly byte[] m_scratch;
    private readonly AutoResetEvent m_dataAvailable = new AutoResetEvent(false);
    private readonly Thread m_writerThread;
    private readonly IFrameInspector? m_inspector;
    private readonly LiveFrameDecoder? m_decoder;
    private readonly bool m_inspects;
    private readonly Thread? m_inspectorThread;
    private readonly GrayImage? m_inspectImage;
    private readonly AutoResetEvent m_frameAvailable = new AutoResetEvent(false);
    private readonly object m_previewLock = new object();
    private readonly byte[]? m_preview;

    // m_head is written by the producer only, m_inspected by the inspection thread only, m_tail by the writer only; all are read across
    // threads with Volatile. tail <= inspected <= head (without an inspector, inspected is not used).
    private long m_head;
    private long m_inspected;
    private long m_tail;

    // Set by the inspection thread: the first sequence to write once the start marker was seen (the pre-roll before it), and the sequence
    // at which writing stops after the end marker (exclusive).
    private long m_writeFrom = long.MaxValue;
    private long m_stopAt = long.MaxValue;
    private volatile bool m_stopRequested;
    private volatile bool m_inspectionDone;
    private long m_nextCaptureIndex;
    private bool m_currentFrameDropped;

    // Frames the source dropped before frames whose records were not written (the ring was full): the next record carries them
    private uint m_pendingSourceDrops;
    private long m_framesDropped;
    private long m_framesDiscarded;
    private volatile bool m_armed;
    private volatile bool m_completing;
    private volatile Exception? m_writerError;
    private long m_lastPreviewTicks = long.MinValue;
    private long m_previewCaptureIndex = -1;
    private bool m_disposed;

    /// <summary>Record the frames themselves into <paramref name="writer"/>.</summary>
    public FrameRecorder(CaptureFileWriter writer, FrameRecorderOptions options, CaptureClock clock, IDeviceTimestampSource? deviceTimestamps = null)
      : this((writer ?? throw new ArgumentNullException(nameof(writer))).Header, writer, null, options, clock, deviceTimestamps) { }

    /// <summary>
    /// Record frames of <paramref name="header"/>'s size: the capture data (every frame's decoded markers, needs
    /// <see cref="FrameRecorderOptions.Decoder"/>) into <paramref name="data"/>, and the frames themselves into <paramref name="frames"/>
    /// when given. At least one of them is needed.
    /// </summary>
    public FrameRecorder(
      CaptureFileHeader header,
      CaptureFileWriter? frames,
      CaptureDataWriter? data,
      FrameRecorderOptions options,
      CaptureClock clock,
      IDeviceTimestampSource? deviceTimestamps = null
    )
    {
      m_header = header ?? throw new ArgumentNullException(nameof(header));
      m_options = options ?? throw new ArgumentNullException(nameof(options));
      m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
      if (frames == null && data == null)
        throw new ArgumentException("Nothing to record into: give the frames writer, the data writer or both");
      if (data != null && options.Decoder == null)
        throw new ArgumentException("The capture data needs a decoder (FrameRecorderOptions.Decoder)", nameof(options));
      m_frames = frames;
      m_data = data;
      m_timestamps = deviceTimestamps;
      m_recordSize = header.RecordSize;
      m_pixelByteCount = header.PixelByteCount;
      m_slotCount = Math.Max(options.RingFrames, 2);
      if ((long)m_slotCount * m_recordSize > Array.MaxLength)
        throw new ArgumentException($"A ring of {m_slotCount} frames of {m_recordSize} bytes is too large", nameof(options));

      m_ring = GC.AllocateUninitializedArray<byte>(m_slotCount * m_recordSize, pinned: true);
      m_dataRing = data != null ? GC.AllocateUninitializedArray<byte>(m_slotCount * CaptureDataRecord.Size, pinned: true) : null;
      m_scratch = GC.AllocateUninitializedArray<byte>(m_pixelByteCount, pinned: true);
      m_preview = options.PreviewInterval > TimeSpan.Zero ? new byte[m_pixelByteCount] : null;
      m_armed = options.StartArmed;
      m_inspector = options.Inspector;
      m_decoder = options.Decoder;
      m_inspects = m_inspector != null || m_decoder != null;
      if (m_inspects)
      {
        m_inspectImage = new GrayImage(header.Width, header.Height);
        m_inspectorThread = new Thread(InspectionLoop)
        {
          Name = "FrameRecorder.Inspector",
          IsBackground = true,
          Priority = ThreadPriority.AboveNormal,
        };
        m_inspectorThread.Start();
      }
      else
      {
        m_inspectionDone = true;
      }
      m_writerThread = new Thread(WriterLoop)
      {
        Name = "FrameRecorder.Writer",
        IsBackground = true,
        Priority = ThreadPriority.AboveNormal,
      };
      m_writerThread.Start();
    }

    public int Width => m_header.Width;
    public int Height => m_header.Height;

    public bool IsArmed => m_armed;

    /// <summary>The inspector found the end marker and its end tail has been inspected: stop the source (later frames are not written).</summary>
    public bool StopRequested => m_stopRequested;

    public FrameRecorderStats Stats
    {
      get
      {
        long head = Volatile.Read(ref m_head);
        long tail = Volatile.Read(ref m_tail);
        return new FrameRecorderStats(
          Interlocked.Read(ref m_nextCaptureIndex),
          m_data?.RecordsWritten ?? m_frames!.RecordsWritten,
          Interlocked.Read(ref m_framesDropped),
          Interlocked.Read(ref m_framesDiscarded),
          (m_data?.BytesWritten ?? 0) + (m_frames?.BytesWritten ?? 0),
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
      // Also while armed: with an inspector, frames only leave the ring once inspected, and a waiting source must not skip any
      if (m_options.WaitWhenFull)
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

    public void EndFrame(long hostTicks, long deviceTicks, uint sourceDrops)
    {
      long captureIndex = m_nextCaptureIndex;
      Volatile.Write(ref m_nextCaptureIndex, captureIndex + 1);

      Span<byte> pixels;
      if (m_currentFrameDropped)
      {
        Interlocked.Increment(ref m_framesDropped);
        // Its record is not written: the next one carries the frames the source dropped before it
        m_pendingSourceDrops = (uint)Math.Min(uint.MaxValue, (long)m_pendingSourceDrops + sourceDrops);
        if (g_logger.IsTraceEnabled)
          g_logger.Trace("Ring full, dropped capture index {0}", captureIndex);
        pixels = m_scratch;
      }
      else
      {
        long head = m_head;
        int offset = SlotOffset(head);
        var slot = m_ring.AsSpan(offset, m_recordSize);
        uint drops = (uint)Math.Min(uint.MaxValue, (long)m_pendingSourceDrops + sourceDrops);
        m_pendingSourceDrops = 0;
        new CaptureRecordHeader(captureIndex, hostTicks, deviceTicks, drops, m_pixelByteCount).Write(slot);
        slot.Slice(CaptureFileHeader.RecordHeaderSize + m_pixelByteCount).Clear();
        pixels = slot.Slice(CaptureFileHeader.RecordHeaderSize, m_pixelByteCount);
        Volatile.Write(ref m_head, head + 1);
        if (m_inspects)
          m_frameAvailable.Set();
        else
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
      m_frameAvailable.Set();
      m_dataAvailable.Set();
      m_inspectorThread?.Join();
      m_writerThread.Join();
      if (m_writerError != null)
        throw new IOException("Writing the capture file failed: " + m_writerError.Message, m_writerError);
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      if (m_writerThread.IsAlive || m_inspectorThread?.IsAlive == true)
      {
        m_completing = true;
        m_frameAvailable.Set();
        m_dataAvailable.Set();
        m_inspectorThread?.Join();
        m_writerThread.Join();
      }
      m_frameAvailable.Dispose();
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
          // Read completion before the counters: once it is seen, the counters read afterwards are final
          bool completing = m_completing && m_inspectionDone;
          bool armed = m_armed;
          // Without inspection every produced frame is ready; with it, only the inspected frames
          long ready = m_inspects ? Volatile.Read(ref m_inspected) : Volatile.Read(ref m_head);
          long tail = m_tail;

          if (armed)
          {
            // Keep the pre-roll, but never discard what the inspector already chose to write (it may just have seen the start marker)
            long keep = Math.Clamp(m_options.PreRollFrames, 0, m_slotCount - 1);
            long discardTo = Math.Min(ready - keep, Volatile.Read(ref m_writeFrom));
            if (discardTo > tail)
            {
              Interlocked.Add(ref m_framesDiscarded, discardTo - tail);
              Volatile.Write(ref m_tail, discardTo);
            }
            if (completing)
              break;
            m_dataAvailable.WaitOne(20);
            continue;
          }

          // Just triggered by the inspector: the pre-roll starts exactly PreRollFrames before the start marker
          long writeFrom = Volatile.Read(ref m_writeFrom);
          if (writeFrom != long.MaxValue && tail < writeFrom)
          {
            Interlocked.Add(ref m_framesDiscarded, writeFrom - tail);
            Volatile.Write(ref m_tail, writeFrom);
            tail = writeFrom;
          }

          // After the end tail: later frames are not written
          long stopAt = Volatile.Read(ref m_stopAt);
          if (tail >= stopAt)
          {
            if (ready > tail)
              Volatile.Write(ref m_tail, ready);
            if (completing)
              break;
            m_dataAvailable.WaitOne(20);
            continue;
          }
          long end = Math.Min(ready, stopAt);

          if (end == tail)
          {
            if (completing)
              break;
            m_dataAvailable.WaitOne(20);
            continue;
          }

          int count = (int)Math.Min(Math.Min(end - tail, m_slotCount - (tail % m_slotCount)), m_options.MaxBatchRecords);
          // A live source drops frames when the ring is full, so late timestamps are given up first; a source that is not live waits
          bool ringPressure = !m_options.WaitWhenFull && (Volatile.Read(ref m_head) - tail) * 2 > m_slotCount;
          int resolved = ResolveDeviceTicks(tail, count, completing || ringPressure, waitTicks);
          if (resolved == 0)
          {
            Thread.Sleep(1);
            continue;
          }

          m_frames?.WriteRecords(m_ring.AsSpan(SlotOffset(tail), resolved * m_recordSize));
          if (m_data != null)
          {
            // The data records get the capture part (with the resolved device timestamps) from the frames' record headers
            for (int i = 0; i < resolved; ++i)
            {
              var header = CaptureRecordHeader.Read(m_ring.AsSpan(SlotOffset(tail + i), CaptureFileHeader.RecordHeaderSize));
              CaptureDataRecord.WriteCapture(
                m_dataRing.AsSpan(DataSlotOffset(tail + i), CaptureDataRecord.Size),
                header.CaptureIndex,
                new TickCount64(header.HostTicks),
                header.HasDeviceTicks ? new TickCount64(header.DeviceTicks) : null,
                header.SourceDrops
              );
            }
            m_data.WriteRecords(m_dataRing.AsSpan(DataSlotOffset(tail), resolved * CaptureDataRecord.Size));
          }
          Volatile.Write(ref m_tail, tail + resolved);
        }
      }
      catch (Exception ex)
      {
        g_logger.Error(ex, "Capture writer failed");
        m_writerError = ex;
      }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Inspection thread
    // ------------------------------------------------------------------------------------------------------------------------------------------

    private void InspectionLoop()
    {
      var image = m_inspectImage!;
      try
      {
        while (true)
        {
          bool completing = m_completing;
          long head = Volatile.Read(ref m_head);
          long next = m_inspected;
          if (next == head)
          {
            if (completing)
              break;
            m_frameAvailable.WaitOne(20);
            continue;
          }

          int offset = SlotOffset(next);
          long captureIndex = CaptureRecordHeader.Read(m_ring.AsSpan(offset, CaptureFileHeader.RecordHeaderSize)).CaptureIndex;
          m_ring.AsSpan(offset + CaptureFileHeader.RecordHeaderSize, m_pixelByteCount).CopyTo(image.Pixels);

          MarkerDecodeResult? decoded = null;
          if (m_decoder != null)
          {
            var decode = m_decoder.Decode(image);
            decoded = decode.Main;
            if (m_dataRing != null)
            {
              var slot = m_dataRing.AsSpan(DataSlotOffset(next), CaptureDataRecord.Size);
              CaptureDataRecord.WriteDecoded(slot, decode.Status, decode.MainBytes, decode.SecondBytes);
            }
          }

          switch (m_inspector?.Inspect(image, captureIndex, decoded) ?? FrameTrigger.None)
          {
            case FrameTrigger.Start when m_armed:
              // Publish where writing starts before leaving armed mode, so the writer never discards the start frame or its pre-roll
              Volatile.Write(ref m_writeFrom, Math.Max(0, next - Math.Clamp(m_options.PreRollFrames, 0, m_slotCount - 1)));
              m_armed = false;
              break;
            case FrameTrigger.End when m_options.StopAtEnd && !m_armed && m_stopAt == long.MaxValue:
              Volatile.Write(ref m_stopAt, next + 1 + Math.Max(0, m_options.EndTailFrames));
              break;
          }

          Volatile.Write(ref m_inspected, next + 1);
          if (next + 1 >= Volatile.Read(ref m_stopAt))
            m_stopRequested = true;
          m_dataAvailable.Set();
        }
      }
      catch (Exception ex)
      {
        g_logger.Error(ex, "Capture inspection failed");
        m_writerError ??= ex;
        // Let the writer finish with what was inspected
        Volatile.Write(ref m_inspected, Volatile.Read(ref m_head));
      }
      finally
      {
        m_inspectionDone = true;
        m_dataAvailable.Set();
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

    private int DataSlotOffset(long sequence) => (int)(sequence % m_slotCount) * CaptureDataRecord.Size;

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
