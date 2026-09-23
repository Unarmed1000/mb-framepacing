//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Draws marker quads with GL immediate mode into the current render target, pixel exact (the marker's top-left origin is flipped to
//* Unity's bottom-left). FrameMarkerOverlay uses it at the end of the frame; call it yourself to draw the marker from your own code.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;

namespace MB.FrameMarker.Unity
{
  public static class FrameMarkerGL
  {
    /// <summary>
    /// An opaque vertex color material without culling or depth test (Hidden/Internal-Colored), or null if the shader is not available
    /// (add it to 'Always Included Shaders' in player builds). Destroy it when done.
    /// </summary>
    public static Material CreateMaterial()
    {
      var shader = Shader.Find("Hidden/Internal-Colored");
      if (shader == null)
        return null;
      var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
      material.SetFloat("_SrcBlend", (float)BlendMode.One);
      material.SetFloat("_DstBlend", (float)BlendMode.Zero);
      material.SetFloat("_Cull", (float)CullMode.Off);
      material.SetFloat("_ZWrite", 0f);
      material.SetFloat("_ZTest", (float)CompareFunction.Always);
      return material;
    }

    /// <summary>
    /// Draw <paramref name="count"/> quads (pixel coordinates of an <paramref name="outputWidth"/> x <paramref name="outputHeight"/> output,
    /// top-left origin) into the current render target with <paramref name="material"/>. Does not allocate.
    /// </summary>
    public static void DrawQuads(Material material, Quad[] quads, int count, int outputWidth, int outputHeight)
    {
      GL.PushMatrix();
      _ = material.SetPass(0);
      GL.LoadPixelMatrix(0f, outputWidth, 0f, outputHeight);
      GL.Begin(GL.QUADS);
      for (int i = 0; i < count; ++i)
      {
        var quad = quads[i];
        GL.Color(quad.Dark ? Color.black : Color.white);
        GL.Vertex3(quad.Left, outputHeight - quad.Top, 0f);
        GL.Vertex3(quad.Right, outputHeight - quad.Top, 0f);
        GL.Vertex3(quad.Right, outputHeight - quad.Bottom, 0f);
        GL.Vertex3(quad.Left, outputHeight - quad.Bottom, 0f);
      }
      GL.End();
      GL.PopMatrix();
    }
  }
}
#endif
