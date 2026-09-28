//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How FrameMarkerOverlay draws the marker. All three draw exactly the same pixels.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
namespace MB.FrameMarker.Unity
{
  public enum FrameMarkerRenderMode
  {
    /// <summary>Pixel aligned quads with GL immediate mode and a vertex color material (FrameMarkerGL). Works everywhere.</summary>
    Geometry,

    /// <summary>A module-resolution texture drawn scaled up with point filtering (FrameMarkerTexture).</summary>
    Bitmap,

    /// <summary>One quad with a dedicated shader that looks the modules up in a texture (FrameMarkerQuad). Needs shader model 3.5.</summary>
    Shader,
  }
}
#endif
