//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker is generated every frame, so nothing on that path may allocate (a Unity game would see it as garbage collector spikes).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  [TestFixture]
  public class AllocationTests
  {
    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly ModuleMatrix m_matrix = new ModuleMatrix();
    private readonly Quad[] m_quads = new Quad[Marker.MaxQuadCount];
    private readonly Vertex[] m_triangles = new Vertex[Marker.MaxTriangleVertexCount];
    private readonly Vertex[] m_indexedVertices = new Vertex[Marker.MaxIndexedVertexCount];
    private readonly int[] m_indices = new int[Marker.MaxIndexCount];
    private readonly byte[] m_payloadBytes = new byte[Marker.MaxEncodedPayloadByteCount];

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
      var origin = Marker.RecommendedOrigin(MarkerKind.Frame, 1920, 1080, options, 2);
      // The span path with stack buffers, as a caller without arrays uses it
      Span<Quad> stackQuads = stackalloc Quad[Marker.MaxQuadCount];
      Span<byte> stackBytes = stackalloc byte[Marker.MaxEncodedPayloadByteCount];
      for (int frame = 0; frame < frames; ++frame)
      {
        var payload = new Payload((ulong)frame, Marker.SecondsToTicks(frame / 60.0), 7, MarkerKind.Frame, 1000 + frame, 166_667, 900 + frame, 80_000);
        written += m_generator.GenerateTriangles(payload, options, origin, m_triangles);
        written += m_generator.GenerateStartTriangles(payload, m_metadata, options, origin, m_triangles);
        written += m_generator.GenerateIndexed(payload, options, origin, m_indexedVertices, m_indices, 16).IndexCount;
        written += m_generator.GenerateStartIndexed(payload, m_metadata, options, origin, m_indexedVertices, m_indices).IndexCount;
        written += m_generator.GenerateQuads(payload.WithKind(MarkerKind.SequenceEnd), options, origin, m_quads);
        written += m_generator.GenerateStartQuads(payload, m_metadata, options, origin, m_quads);
        written += m_generator.GenerateModules(payload, m_matrix) ? 1 : 0;
        written += Marker.EncodePayload(payload, m_metadata, m_payloadBytes);
        written += SequenceId.FromGuid(g_guid).IsEmpty ? 0 : 1;
        written += SequenceId.TryFromText("camera pan", out var tag) && !tag.IsEmpty ? 1 : 0;
        int quadCount = m_generator.GenerateQuads(payload, options, origin, m_quads);
        written += Marker.QuadsToTriangles(m_quads.AsSpan(0, quadCount), m_triangles);
        written += Marker.QuadsToIndexed(m_quads.AsSpan(0, quadCount), m_indexedVertices, m_indices).IndexCount;
        written += m_generator.GenerateStartQuads(payload, m_metadata, options, origin, stackQuads);
        int byteCount = Marker.EncodePayload(payload.WithKind(MarkerKind.SequenceStart), m_metadata, stackBytes);
        written += Marker.TryDecodePayload(stackBytes.Slice(0, byteCount), out _, out var decoded) && !decoded.SequenceId.IsEmpty ? 1 : 0;
      }
      return written;
    }
  }
}
