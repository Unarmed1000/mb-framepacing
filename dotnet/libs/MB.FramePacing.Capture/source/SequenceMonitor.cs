//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Watches the recorder's preview frames (a copy every ~50ms) for start/end markers while capturing. Finds the marker once with a full
//* search, then decodes the locked region, which is cheap enough to keep up easily.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public sealed class SequenceMonitor
  {
    private readonly MarkerDecoder m_searchDecoder = new MarkerDecoder(tryHarder: true);
    private readonly MarkerDecoder m_lockedDecoder = new MarkerDecoder();
    private MarkerLock? m_lock;

    /// <summary>The newest decoded marker, if any.</summary>
    public MarkerDecodeResult? Last { get; private set; }

    /// <summary>The first start marker seen (run id + metadata).</summary>
    public MarkerDecodeResult? Start { get; private set; }

    /// <summary>True once an end marker of the started run (or of any run, if no start was seen) has been seen.</summary>
    public bool EndSeen { get; private set; }

    public MarkerLock? Lock => m_lock;

    /// <summary>Capture index of the last inspected preview frame (maintained by the caller).</summary>
    public long LastInspectedIndex { get; set; } = -1;

    /// <summary>Inspect one preview frame. Returns the decoded marker or null.</summary>
    public MarkerDecodeResult? Inspect(GrayImage frame)
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
      if (!result.IsDecoded)
        return null;

      Last = result;
      switch (result.Payload.Kind)
      {
        case MarkerKind.SequenceStart:
          Start ??= result;
          break;
        case MarkerKind.SequenceEnd:
          if (Start == null || Start.Value.Payload.RunId == result.Payload.RunId)
            EndSeen = true;
          break;
      }
      return result;
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
