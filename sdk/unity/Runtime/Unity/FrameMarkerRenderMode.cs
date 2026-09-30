//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How FrameMarkerOverlay draws the marker, fastest first. All draw exactly the same pixels; ShaderPackedBits is the default.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
namespace MB.FramePacing.Marker.Unity
{
  public enum FrameMarkerRenderMode
  {
    /// <summary>
    /// One quad with a dedicated shader that reads each module's bit from the module matrix's packed bits (FrameMarkerQuad, 211 bytes per
    /// frame): the fastest, the default. Needs shader model 3.5 with integers; without it the overlay draws Geometry.
    /// </summary>
    ShaderPackedBits,

    /// <summary>
    /// One quad with a dedicated shader that looks the modules up in a texture of one texel per module (FrameMarkerQuad, 1681 bytes per
    /// frame). Needs shader model 3.5; without it the overlay draws Geometry.
    /// </summary>
    Shader,

    /// <summary>A module-resolution texture drawn scaled up with point filtering (FrameMarkerTexture).</summary>
    Bitmap,

    /// <summary>Pixel aligned quads with GL immediate mode and a vertex color material (FrameMarkerGL). Works everywhere.</summary>
    Geometry,
  }
}
#endif
