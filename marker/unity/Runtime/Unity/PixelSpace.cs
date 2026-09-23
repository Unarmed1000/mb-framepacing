//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Converts the marker's pixel coordinates (origin top-left, +y down) to Unity's screen pixels (origin bottom-left, +y up).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  public static class PixelSpace
  {
    /// <summary>The vertex in Unity screen pixels for an output that is <paramref name="outputHeight"/> pixels high.</summary>
    public static Vector3 ToUnity(in Vertex vertex, int outputHeight) => new Vector3(vertex.X, outputHeight - vertex.Y, 0f);

    /// <summary>The vertex color: pure black or pure white, fully opaque.</summary>
    public static Color32 ToColor32(in Vertex vertex) => new Color32(vertex.Luma, vertex.Luma, vertex.Luma, 255);

    /// <summary>An orthographic projection that maps Unity screen pixels 1:1 onto a <paramref name="width"/> x <paramref name="height"/> target.</summary>
    public static Matrix4x4 Projection(int width, int height) => Matrix4x4.Ortho(0f, width, 0f, height, -1f, 1f);
  }
}
#endif
