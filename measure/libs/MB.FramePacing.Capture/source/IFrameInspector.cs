//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Looks at every captured frame, in capture order, before the recorder writes or discards it (the start/end marker triggers).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  public interface IFrameInspector
  {
    /// <summary>
    /// Inspect one frame. Called on the recorder's inspection thread for every frame that reached the ring, in capture order; the image is
    /// reused for the next frame. A slow inspector holds the frames back (a live source then drops frames once the ring is full).
    /// </summary>
    /// <param name="decoded">
    /// The frame's main marker as the recorder's decoder read it (<see cref="MarkerDecodeResult.NotFound"/> when there was none), or null
    /// when the recorder has no decoder.
    /// </param>
    FrameTrigger Inspect(GrayImage frame, long captureIndex, MarkerDecodeResult? decoded);
  }
}
