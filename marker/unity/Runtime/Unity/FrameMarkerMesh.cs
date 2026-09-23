//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Keeps a Mesh with the current frame's marker, for drawing it through your own render pipeline code (a URP renderer feature, an HDRP
//* custom pass or a CommandBuffer) instead of FrameMarkerOverlay. Draw the mesh last, with PixelSpace.Projection, an unlit vertex color
//* material without blending, depth test or culling. Update never allocates.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  public sealed class FrameMarkerMesh : IDisposable
  {
    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly Vertex[] m_vertices = new Vertex[Marker.MaxIndexedVertexCount];
    private readonly int[] m_indices = new int[Marker.MaxIndexCount];
    private readonly Vector3[] m_positions = new Vector3[Marker.MaxIndexedVertexCount];
    private readonly Color32[] m_colors = new Color32[Marker.MaxIndexedVertexCount];

    public FrameMarkerMesh()
    {
      Mesh = new Mesh { name = "MB Frame Marker", hideFlags = HideFlags.HideAndDontSave };
      Mesh.MarkDynamic();
    }

    /// <summary>The marker in Unity screen pixels (origin bottom-left, +y up).</summary>
    public Mesh Mesh { get; private set; }

    /// <summary>
    /// Fill the mesh with the marker for an output of <paramref name="outputHeight"/> pixels. <paramref name="start"/> is only used by start
    /// markers. Returns false (and leaves the mesh unchanged) if the options are invalid or the start name is too long.
    /// </summary>
    public bool Update(in Payload payload, in StartMetadata start, in Options options, Point origin, int outputHeight)
    {
      var count =
        payload.Kind == MarkerKind.SequenceStart
          ? m_generator.GenerateStartIndexed(payload, start, options, origin, m_vertices, m_indices)
          : m_generator.GenerateIndexed(payload, options, origin, m_vertices, m_indices);
      if (count.IndexCount == 0 || Mesh == null)
        return false;

      for (int i = 0; i < count.VertexCount; ++i)
      {
        m_positions[i] = PixelSpace.ToUnity(m_vertices[i], outputHeight);
        m_colors[i] = PixelSpace.ToColor32(m_vertices[i]);
      }
      Mesh.Clear(true);
      Mesh.SetVertices(m_positions, 0, count.VertexCount);
      Mesh.SetColors(m_colors, 0, count.VertexCount);
      Mesh.SetIndices(m_indices, 0, count.IndexCount, MeshTopology.Triangles, 0);
      return true;
    }

    public void Dispose()
    {
      if (Mesh == null)
        return;
      if (Application.isPlaying)
        UnityEngine.Object.Destroy(Mesh);
      else
        UnityEngine.Object.DestroyImmediate(Mesh);
      Mesh = null;
    }
  }
}
#endif
