//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker is generated every frame, so nothing on that path may allocate (a Unity game would see it as garbage collector spikes).
//*
//* (c) 2026 Mana Battery
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

    // The longest name (64 bytes as UTF-8, including two byte characters)
    private readonly StartMetadata m_metadata = new StartMetadata(
      638_000_000_000_000_000,
      "allocation-test æøå 01234567890123456789012345678901234567890"
    );

    [Test]
    public void TheMetadataNameIsTheLongestAllowed()
    {
      Assert.That(System.Text.Encoding.UTF8.GetByteCount(m_metadata.Name), Is.EqualTo(Marker.MaxStartNameBytes));
    }

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
      var origin = Marker.RecommendedOrigin(MarkerSlot.TopLeft, 1920, 1080, options, 2);
      for (int frame = 0; frame < frames; ++frame)
      {
        var payload = new Payload((ulong)frame, Marker.SecondsToTicks(frame / 60.0), 7);
        written += m_generator.GenerateTriangles(payload, options, origin, m_triangles);
        written += m_generator.GenerateStartTriangles(payload, m_metadata, options, origin, m_triangles);
        written += m_generator.GenerateIndexed(payload, options, origin, m_indexedVertices, m_indices, 16).IndexCount;
        written += m_generator.GenerateStartIndexed(payload, m_metadata, options, origin, m_indexedVertices, m_indices).IndexCount;
        written += m_generator.GenerateQuads(payload.WithKind(MarkerKind.SequenceEnd), options, origin, m_quads);
        written += m_generator.GenerateStartQuads(payload, m_metadata, options, origin, m_quads);
        written += m_generator.GenerateModules(payload, m_matrix) ? 1 : 0;
        written += Marker.EncodePayload(payload, m_metadata, m_payloadBytes);
        int quadCount = m_generator.GenerateQuads(payload, options, origin, m_quads);
        written += Marker.QuadsToTriangles(m_quads, quadCount, m_triangles);
        written += Marker.QuadsToIndexed(m_quads, quadCount, m_indexedVertices, m_indices).IndexCount;
      }
      return written;
    }
  }
}
