//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds the start and end markers of a run. As the recorder's inspector it sees every captured frame in order, so a marker in a single
//* frame is enough. Finds the marker once with a full search, then decodes the locked region, which is cheap enough to keep up.
//* The results are read from other threads (progress, capture.json), so they are guarded by a lock.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public sealed class SequenceMonitor : IFrameInspector
  {
    private readonly MarkerDecoder m_searchDecoder = new MarkerDecoder(tryHarder: true);
    private readonly MarkerDecoder m_lockedDecoder;
    private readonly object m_sync = new object();
    private MarkerLock? m_lock;
    private MarkerDecodeResult? m_last;
    private MarkerDecodeResult? m_start;
    private bool m_endSeen;

    /// <summary>The newest decoded marker, if any.</summary>
    public MarkerDecodeResult? Last
    {
      get
      {
        lock (m_sync)
          return m_last;
      }
    }

    /// <summary>The first start marker seen (run id + metadata).</summary>
    public MarkerDecodeResult? Start
    {
      get
      {
        lock (m_sync)
          return m_start;
      }
    }

    /// <summary>True once an end marker of the started run (or of any run, if no start was seen) has been seen.</summary>
    public bool EndSeen
    {
      get
      {
        lock (m_sync)
          return m_endSeen;
      }
    }

    /// <param name="knownLock">Where the marker is, when that is known up front (camera captures store it at a fixed place).</param>
    /// <param name="sampleModuleGrid">EXPERIMENTAL camera captures: decode the soft rectified markers by sampling the module grid.</param>
    public SequenceMonitor(MarkerLock? knownLock = null, bool sampleModuleGrid = false)
    {
      m_lock = knownLock;
      m_lockedDecoder = new MarkerDecoder(sampleModuleGrid: sampleModuleGrid);
    }

    public MarkerLock? Lock => m_lock;

    /// <summary>
    /// Inspect one frame. Returns <see cref="FrameTrigger.Start"/> for the first start marker and <see cref="FrameTrigger.End"/> for the first
    /// end marker of that run (of any run when the capture began after the start marker).
    /// </summary>
    public FrameTrigger Inspect(GrayImage frame, long captureIndex)
    {
      var result = Decode(frame);
      if (result == null)
        return FrameTrigger.None;

      lock (m_sync)
      {
        m_last = result;
        var payload = result.Value.Payload;
        switch (payload.Kind)
        {
          case MarkerKind.SequenceStart when m_start == null:
            m_start = result;
            return FrameTrigger.Start;
          case MarkerKind.SequenceEnd when !m_endSeen && (m_start == null || m_start.Value.Payload.RunId == payload.RunId):
            m_endSeen = true;
            return FrameTrigger.End;
          default:
            return FrameTrigger.None;
        }
      }
    }

    /// <summary>Decode the marker in one frame (locked region once the marker was found). Returns null when there is none.</summary>
    private MarkerDecodeResult? Decode(GrayImage frame)
    {
      MarkerDecodeResult result;
      if (m_lock is { } markerLock)
        result = m_lockedDecoder.DecodeLocked(frame, markerLock);
      else
      {
        result = m_searchDecoder.Decode(frame);
        if (result.IsDecoded && result.ModuleSizePx > 0)
          m_lock = LockFor(result);
      }
      return result.IsDecoded ? result : null;
    }

    /// <summary>
    /// All markers are drawn at the same origin; the frame marker lock is the origin plus the frame marker size, whichever marker kind was found.
    /// </summary>
    public static MarkerLock LockFor(MarkerDecodeResult result)
    {
      int size = (int)System.Math.Round(MarkerRenderer.MarkerSizePx(1) * result.ModuleSizePx);
      return new MarkerLock(new PixelRect(result.Bounds.X, result.Bounds.Y, size, size), result.ModuleSizePx);
    }
  }
}
