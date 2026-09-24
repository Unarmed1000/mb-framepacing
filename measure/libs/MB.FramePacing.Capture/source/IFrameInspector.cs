//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Looks at every captured frame, in capture order, before the recorder writes or discards it (the start/end marker triggers).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public interface IFrameInspector
  {
    /// <summary>
    /// Inspect one frame. Called on the recorder's inspection thread for every frame that reached the ring, in capture order; the image is
    /// reused for the next frame. A slow inspector holds the frames back (a live source then drops frames once the ring is full).
    /// </summary>
    FrameTrigger Inspect(GrayImage frame, long captureIndex);
  }
}
