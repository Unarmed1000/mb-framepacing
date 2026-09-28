//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Draws the marker as one opaque quad with a dedicated shader (Hidden/MB/FrameMarkerQuad): per frame only a 41x41 module texture (1681
//* bytes) changes; the quad and its material stay. Encode the marker with MarkerGenerator.TryGenerateModules and pass the matrix to Update,
//* then DrawNow (GL immediate mode, the way FrameMarkerOverlay draws) or draw Mesh with Material from your own command buffer, with
//* PixelSpace.Projection. Needs shader model 3.5; add the shader to 'Always Included Shaders' in player builds. Update never allocates.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  public sealed class FrameMarkerQuad : IDisposable
  {
    /// <summary>The shader's name, for Shader.Find and 'Always Included Shaders'.</summary>
    public const string ShaderName = "Hidden/MB/FrameMarkerQuad";

    private static readonly int g_modules = Shader.PropertyToID("_Modules");
    private static readonly int g_moduleSizePx = Shader.PropertyToID("_ModuleSizePx");
    private static readonly int g_quietZoneModules = Shader.PropertyToID("_QuietZoneModules");
    private static readonly int g_size = Shader.PropertyToID("_Size");

    private readonly byte[] m_texels = new byte[Marker.QrModuleCount * Marker.QrModuleCount];
    private readonly Vector3[] m_positions = new Vector3[4];
    private readonly Vector2[] m_uvs = new Vector2[4];
    private readonly Vertex[] m_corners = new Vertex[4];
    private Texture2D m_texture;
    private Point m_origin;
    private int m_markerSize = -1;
    private int m_outputHeight = -1;

    /// <summary>A quad with the shader, or an instance whose <see cref="Material"/> is null when the shader is not available.</summary>
    public FrameMarkerQuad()
    {
      var shader = Shader.Find(ShaderName);
      if (shader == null || !shader.isSupported)
        return;
      Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
      m_texture = new Texture2D(Marker.QrModuleCount, Marker.QrModuleCount, TextureFormat.R8, false, true)
      {
        name = "MB Frame Marker Modules",
        hideFlags = HideFlags.HideAndDontSave,
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp,
      };
      Material.SetTexture(g_modules, m_texture);
      Mesh = new Mesh { name = "MB Frame Marker Quad", hideFlags = HideFlags.HideAndDontSave };
      Mesh.SetVertices(m_positions);
      Mesh.SetUVs(0, m_uvs);
      Mesh.SetIndices(new[] { 0, 1, 3, 3, 1, 2 }, MeshTopology.Triangles, 0);
    }

    /// <summary>The material with the module texture and the marker's size, or null when the shader is not available.</summary>
    public Material Material { get; private set; }

    /// <summary>The quad in Unity screen pixels (origin bottom-left, +y up), UV the marker-local pixel coordinate.</summary>
    public Mesh Mesh { get; private set; }

    /// <summary>
    /// Show the encoded marker at <paramref name="origin"/> (top-left, pixels) in an output of <paramref name="outputHeight"/> pixels. The
    /// quad is rebuilt only when its size or place changes. Returns false if the shader is not available, the options are invalid or the
    /// matrix is empty.
    /// </summary>
    public bool Update(ModuleMatrix matrix, in Options options, Point origin, int outputHeight)
    {
      if (Material == null || matrix.IsEmpty || !Marker.IsValid(options))
        return false;
      // One texel per module, top row first (the shader loads texel (column, row) directly)
      if (!Marker.ModulesToBitmap(matrix, new Options(1, 0), default, m_texels, Marker.QrModuleCount, Marker.QrModuleCount, PixelFormat.Gray8))
        return false;
      m_texture.SetPixelData(m_texels, 0);
      m_texture.Apply(false);
      Material.SetFloat(g_moduleSizePx, options.ModuleSizePx);
      Material.SetFloat(g_quietZoneModules, options.QuietZoneModules);
      Material.SetFloat(g_size, matrix.Size);

      int markerSize = (matrix.Size + (2 * options.QuietZoneModules)) * options.ModuleSizePx;
      if (markerSize != m_markerSize || !origin.Equals(m_origin) || outputHeight != m_outputHeight)
      {
        (m_markerSize, m_origin, m_outputHeight) = (markerSize, origin, outputHeight);
        m_corners[0] = new Vertex(origin.X, origin.Y, 255);
        m_corners[1] = new Vertex(origin.X + markerSize, origin.Y, 255);
        m_corners[2] = new Vertex(origin.X + markerSize, origin.Y + markerSize, 255);
        m_corners[3] = new Vertex(origin.X, origin.Y + markerSize, 255);
        for (int i = 0; i < 4; ++i)
        {
          m_positions[i] = PixelSpace.ToUnity(m_corners[i], outputHeight);
          m_uvs[i] = new Vector2(m_corners[i].X - origin.X, m_corners[i].Y - origin.Y);
        }
        Mesh.SetVertices(m_positions);
        Mesh.SetUVs(0, m_uvs);
        Mesh.RecalculateBounds();
      }
      return true;
    }

    /// <summary>Draw the quad now with GL immediate mode into the current render target (<paramref name="outputWidth"/> pixels wide).</summary>
    public void DrawNow(int outputWidth)
    {
      if (Material == null || m_markerSize < 0)
        return;
      GL.PushMatrix();
      GL.LoadPixelMatrix(0f, outputWidth, 0f, m_outputHeight);
      Material.SetPass(0);
      GL.Begin(GL.QUADS);
      for (int i = 0; i < 4; ++i)
      {
        GL.TexCoord2(m_uvs[i].x, m_uvs[i].y);
        GL.Vertex(m_positions[i]);
      }
      GL.End();
      GL.PopMatrix();
    }

    public void Dispose()
    {
      Destroy(Material);
      Destroy(m_texture);
      Destroy(Mesh);
      Material = null;
      m_texture = null;
      Mesh = null;
    }

    private static void Destroy(UnityEngine.Object value)
    {
      if (value == null)
        return;
      if (Application.isPlaying)
        UnityEngine.Object.Destroy(value);
      else
        UnityEngine.Object.DestroyImmediate(value);
    }
  }
}
#endif
