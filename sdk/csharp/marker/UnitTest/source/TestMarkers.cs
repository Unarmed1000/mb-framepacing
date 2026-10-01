//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Encode, then draw: what a caller does each frame (MarkerGenerator.TryGenerateModules, then a FrameMarker.ModulesTo... output), in one call for
//* the geometry tests. A start payload is encoded with its metadata.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  public static class TestMarkers
  {
    private static readonly MarkerGenerator g_generator = new MarkerGenerator();
    private static readonly byte[] g_bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];

    /// <summary>The payload's module matrix, in bytes the helper owns (valid until the next call).</summary>
    public static ModuleMatrix Encode(in Payload payload, in StartMetadata metadata = default)
    {
      Assert.That(g_generator.TryGenerateModules(payload, metadata, g_bits, out var matrix), Is.True, "encoding failed");
      return matrix;
    }

    /// <summary>The documented vertex order of a quad, (TL, TR, BL) (BL, TR, BR), written independently of the library.</summary>
    public static List<Vertex> ToTriangles(IEnumerable<MarkerQuad> quads)
    {
      var vertices = new List<Vertex>();
      foreach (var quad in quads)
      {
        byte luma = quad.Dark ? (byte)0 : (byte)255;
        vertices.Add(new Vertex(quad.Rect.Left, quad.Rect.Top, luma));
        vertices.Add(new Vertex(quad.Rect.Right, quad.Rect.Top, luma));
        vertices.Add(new Vertex(quad.Rect.Left, quad.Rect.Bottom, luma));
        vertices.Add(new Vertex(quad.Rect.Left, quad.Rect.Bottom, luma));
        vertices.Add(new Vertex(quad.Rect.Right, quad.Rect.Top, luma));
        vertices.Add(new Vertex(quad.Rect.Right, quad.Rect.Bottom, luma));
      }
      return vertices;
    }

    /// <summary>The documented indexed order: 4 vertices (TL, TR, BR, BL) and indices (0,1,3)(3,1,2) per quad, plus the base vertex.</summary>
    public static (List<Vertex> Vertices, List<int> Indices) ToIndexed(IEnumerable<MarkerQuad> quads, int baseVertex)
    {
      var vertices = new List<Vertex>();
      var indices = new List<int>();
      foreach (var quad in quads)
      {
        byte luma = quad.Dark ? (byte)0 : (byte)255;
        int first = baseVertex + vertices.Count;
        vertices.Add(new Vertex(quad.Rect.Left, quad.Rect.Top, luma));
        vertices.Add(new Vertex(quad.Rect.Right, quad.Rect.Top, luma));
        vertices.Add(new Vertex(quad.Rect.Right, quad.Rect.Bottom, luma));
        vertices.Add(new Vertex(quad.Rect.Left, quad.Rect.Bottom, luma));
        indices.AddRange(new[] { first, first + 1, first + 3, first + 3, first + 1, first + 2 });
      }
      return (vertices, indices);
    }

    public static int GenerateQuads(in Payload payload, in Options options, Point origin, Span<MarkerQuad> destination) =>
      FrameMarker.ModulesToQuads(Encode(payload), options, origin, destination);

    public static int GenerateStartQuads(
      in Payload payload,
      in StartMetadata metadata,
      in Options options,
      Point origin,
      Span<MarkerQuad> destination
    ) => FrameMarker.ModulesToQuads(Encode(payload.WithKind(MarkerKind.SequenceStart), metadata), options, origin, destination);

    public static int GenerateTriangles(in Payload payload, in Options options, Point origin, Span<Vertex> destination) =>
      FrameMarker.ModulesToTriangles(Encode(payload), options, origin, destination);

    public static int GenerateStartTriangles(
      in Payload payload,
      in StartMetadata metadata,
      in Options options,
      Point origin,
      Span<Vertex> destination
    ) => FrameMarker.ModulesToTriangles(Encode(payload.WithKind(MarkerKind.SequenceStart), metadata), options, origin, destination);

    public static IndexedCount GenerateIndexed(
      in Payload payload,
      in Options options,
      Point origin,
      Span<Vertex> vertices,
      Span<int> indices,
      int baseVertex = 0
    ) => FrameMarker.ModulesToIndexed(Encode(payload), options, origin, vertices, indices, baseVertex);

    public static IndexedCount GenerateStartIndexed(
      in Payload payload,
      in StartMetadata metadata,
      in Options options,
      Point origin,
      Span<Vertex> vertices,
      Span<int> indices,
      int baseVertex = 0
    ) => FrameMarker.ModulesToIndexed(Encode(payload.WithKind(MarkerKind.SequenceStart), metadata), options, origin, vertices, indices, baseVertex);
  }
}
