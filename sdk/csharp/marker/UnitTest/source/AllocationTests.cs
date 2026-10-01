//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker is generated every frame, so nothing on that path may allocate (a Unity game would see it as garbage collector spikes).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class AllocationTests
  {
    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly byte[] m_bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private readonly byte[] m_pixels = new byte[294 * 294 * 4];
    private readonly MarkerQuad[] m_quads = new MarkerQuad[FrameMarker.MaxQuadCount];
    private readonly Vertex[] m_triangles = new Vertex[FrameMarker.MaxTriangleVertexCount];
    private readonly Vertex[] m_indexedVertices = new Vertex[FrameMarker.MaxIndexedVertexCount];
    private readonly int[] m_indices = new int[FrameMarker.MaxIndexCount];
    private readonly Vertex[] m_grid = new Vertex[FrameMarker.MaxGridVertexCount];
    private readonly byte[] m_payloadBytes = new byte[Payload.MaxEncodedByteCount];

    private static readonly Guid g_guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

    // A start marker with every byte of its sequence id in use
    private readonly StartMetadata m_metadata = new StartMetadata(
      638_000_000_000_000_000,
      new SequenceId(0x0123_4567_89AB_CDEF, 0xFEDC_BA98_7654_3210)
    );

    [Test]
    public void GeneratingMarkers_DoesNotAllocate()
    {
      long written = RunFrames(10); // warm up (JIT, static tables)
      long before = GC.GetAllocatedBytesForCurrentThread();
      written += RunFrames(1000);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.That(allocated, Is.Zero);
      Assert.That(written, Is.Positive, "the calls must actually have produced output");
    }

    private long RunFrames(int frames)
    {
      long written = 0;
      var options = Options.Default;
      var origin = options.RecommendedOrigin(MarkerKind.Frame, 1080, 2);
      // The span path with stack buffers, as a caller without arrays uses it
      Span<MarkerQuad> stackQuads = stackalloc MarkerQuad[FrameMarker.MaxQuadCount];
      Span<byte> stackBytes = stackalloc byte[Payload.MaxEncodedByteCount];
      Span<byte> stackBits = stackalloc byte[ModuleMatrix.MaxPackedModuleByteCount];
      for (int frame = 0; frame < frames; ++frame)
      {
        var payload = new Payload(
          MarkerKind.Frame,
          7,
          (ulong)frame,
          MarkerFlags.StaticAfter,
          TimeSpanUtil.FromSeconds(frame / 60.0),
          preferredFrameTime: new TimeSpan32(166_667),
          targetFrameTime: new TimeSpan32(166_667),
          intendedDisplayTime: new TickCount64(1000 + frame),
          cpuStartTime: new TickCount64(900 + frame),
          cpuBusy: new TimeSpan32(80_000)
        );
        if (m_generator.TryGenerateModules(payload, m_bits, out var matrix))
        {
          written += FrameMarker.ModulesToTriangles(matrix, options, origin, m_triangles);
          written += FrameMarker.ModulesToIndexed(matrix, options, origin, m_indexedVertices, m_indices, 16).IndexCount;
          written += FrameMarker.ModulesToQuads(matrix, options, origin, m_quads);
          written += FrameMarker.GridVertices(MarkerKind.Frame, options, origin, m_grid);
          written += FrameMarker.ModulesToGridIndices(matrix, m_indices, 32);
          written += FrameMarker.ModulesToBitmap(matrix, options, default, m_pixels, 294, 294, PixelFormat.R8G8B8A8) ? 1 : 0;
        }
        if (m_generator.TryGenerateModules(payload.WithKind(MarkerKind.SequenceStart), m_metadata, stackBits, out var start))
        {
          written += FrameMarker.ModulesToTriangles(start, options, origin, m_triangles);
          written += FrameMarker.ModulesToQuads(start, options, origin, stackQuads);
          written += FrameMarker.ModulesToBitmap(start, new Options(1, 0), default, m_pixels, 41, 41, PixelFormat.R8) ? 1 : 0;
        }
        written += m_generator.TryGenerateModules(payload.WithKind(MarkerKind.Sync), stackBits, out var sync) ? sync.Bits.Length : 0;
        written += FrameMarker.EncodePayload(payload, m_metadata, m_payloadBytes);
        written += SequenceId.FromGuid(g_guid).IsEmpty ? 0 : 1;
        written += SequenceId.TryFromText("camera pan", out var tag) && !tag.IsEmpty ? 1 : 0;
        int byteCount = FrameMarker.EncodePayload(payload.WithKind(MarkerKind.SequenceStart), m_metadata, stackBytes);
        written += FrameMarker.TryDecodePayload(stackBytes.Slice(0, byteCount), out _, out var decoded) && !decoded.SequenceId.IsEmpty ? 1 : 0;
      }
      return written;
    }
  }
}
