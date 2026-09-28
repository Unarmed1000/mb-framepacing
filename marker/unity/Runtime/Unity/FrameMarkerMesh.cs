//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Keeps a Mesh with the current frame's marker, for drawing it through your own render pipeline code (a URP renderer feature, an HDRP
//* custom pass or a CommandBuffer) instead of FrameMarkerOverlay. Encode the marker with MarkerGenerator.TryGenerateModules and pass the
//* matrix to Update. Draw the mesh last, with PixelSpace.Projection, an unlit vertex color material without blending, depth test or culling.
//* The vertices are a static grid; each frame only uploads indices (about 5 KB). Update never allocates.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  public sealed class FrameMarkerMesh : IDisposable
  {
    private readonly Vertex[] m_grid = new Vertex[Marker.MaxGridVertexCount];
    private readonly int[] m_indices = new int[Marker.MaxIndexCount];
    private readonly Vector3[] m_positions = new Vector3[Marker.MaxGridVertexCount];
    private readonly Color32[] m_colors = new Color32[Marker.MaxGridVertexCount];
    private int m_gridSize;
    private Options m_gridOptions;
    private Point m_gridOrigin;
    private int m_gridOutputHeight;

    public FrameMarkerMesh()
    {
      Mesh = new Mesh { name = "MB Frame Marker", hideFlags = HideFlags.HideAndDontSave };
      Mesh.MarkDynamic();
    }

    /// <summary>The marker in Unity screen pixels (origin bottom-left, +y up).</summary>
    public Mesh Mesh { get; private set; }

    /// <summary>
    /// Fill the mesh with the encoded marker (<see cref="MarkerGenerator.TryGenerateModules(in Payload, in StartMetadata, Span{byte}, out ModuleMatrix)"/>)
    /// for an output of <paramref name="outputHeight"/> pixels. The vertices are the static grid (<see cref="Marker.GridVertices"/>), set
    /// again only when the symbol size, the options, the origin or the output height change; every other frame only the indices are
    /// uploaded. Returns false (and leaves the mesh unchanged) if the options are invalid or the matrix is empty.
    /// </summary>
    public bool Update(ModuleMatrix matrix, in Options options, Point origin, int outputHeight)
    {
      if (Mesh == null || matrix.IsEmpty || !Marker.IsValid(options))
        return false;
      bool sameGrid =
        matrix.Size == m_gridSize
        && options.ModuleSizePx == m_gridOptions.ModuleSizePx
        && options.QuietZoneModules == m_gridOptions.QuietZoneModules
        && origin.Equals(m_gridOrigin)
        && outputHeight == m_gridOutputHeight;
      if (!sameGrid)
      {
        var kind = matrix.Size == Marker.SyncQrModuleCount ? MarkerKind.Sync : MarkerKind.Frame;
        int vertexCount = Marker.GridVertices(kind, options, origin, m_grid);
        for (int i = 0; i < vertexCount; ++i)
        {
          m_positions[i] = PixelSpace.ToUnity(m_grid[i], outputHeight);
          m_colors[i] = PixelSpace.ToColor32(m_grid[i]);
        }
        Mesh.Clear(false);
        Mesh.SetVertices(m_positions, 0, vertexCount);
        Mesh.SetColors(m_colors, 0, vertexCount);
        (m_gridSize, m_gridOptions, m_gridOrigin, m_gridOutputHeight) = (matrix.Size, options, origin, outputHeight);
      }
      int indexCount = Marker.ModulesToGridIndices(matrix, m_indices);
      Mesh.SetIndices(m_indices, 0, indexCount, MeshTopology.Triangles, 0, calculateBounds: !sameGrid);
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
