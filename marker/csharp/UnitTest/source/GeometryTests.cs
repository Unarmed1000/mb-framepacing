//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sizes, placement, symbol versions, the vertex order of every output and the failure cases. The values match the C++ tests.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Linq;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  [TestFixture]
  public class GeometryTests
  {
    [Test]
    public void BufferSizes_MatchTheCppLibrary()
    {
      Assert.That(Marker.MaxFrameQuadCount, Is.EqualTo(326));
      Assert.That(Marker.MaxQuadCount, Is.EqualTo(862));
      Assert.That(Marker.MaxFrameTriangleVertexCount, Is.EqualTo(326 * 6));
      Assert.That(Marker.MaxIndexCount, Is.EqualTo(862 * 6));
      Assert.That(Marker.MaxEncodedPayloadByteCount, Is.EqualTo(97));
    }

    [Test]
    public void MarkerSize()
    {
      Assert.That(Marker.MarkerSizePx(Options.Default), Is.EqualTo(198));
      Assert.That(Marker.MarkerSizePx(new Options(3)), Is.EqualTo(99));
      Assert.That(Marker.MarkerSizePx(new Options(12)), Is.EqualTo(396));
      Assert.That(Marker.MarkerSizePx(new Options(1, 0)), Is.EqualTo(25));
      Assert.That(Marker.MaxMarkerSizePx(Options.Default), Is.EqualTo(294));
    }

    [Test]
    public void ModuleSizeRecommendations_MatchTheDocumentation()
    {
      Assert.That(Marker.MinimumModuleSizePx(1080, 1080), Is.EqualTo(2));
      Assert.That(Marker.RecommendModuleSizePx(1080, 1080), Is.EqualTo(3));
      Assert.That(Marker.RecommendModuleSizePx(1080, 1080, mjpeg: true), Is.EqualTo(4));
      Assert.That(Marker.MinimumModuleSizePx(1440, 1080), Is.EqualTo(3));
      Assert.That(Marker.RecommendModuleSizePx(1440, 1080), Is.EqualTo(4));
      Assert.That(Marker.MinimumModuleSizePx(1080, 540), Is.EqualTo(4));
      Assert.That(Marker.RecommendModuleSizePx(1080, 540), Is.EqualTo(6));
      Assert.That(Marker.RecommendModuleSizePx(2160, 1080), Is.EqualTo(6));
      Assert.That(Marker.RecommendModuleSizePx(1080, 540, mjpeg: true), Is.EqualTo(8));
      Assert.That(Marker.MinimumModuleSizePx(1080, 360), Is.EqualTo(6));
      Assert.That(Marker.RecommendModuleSizePx(1080, 360), Is.EqualTo(9));
      Assert.That(Marker.MinimumModuleSizePx(2160, 540), Is.EqualTo(8));
      Assert.That(Marker.RecommendModuleSizePx(2160, 540), Is.EqualTo(12));
      Assert.That(Marker.RecommendModuleSizePx(540, 1080), Is.EqualTo(3));
      Assert.That(Marker.RecommendModuleSizePx(0, 1080), Is.EqualTo(3));
    }

    [Test]
    public void RecommendedOrigins()
    {
      var options = Options.Default;
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.TopLeft, 1920, 1080, options), Is.EqualTo(new Point(32, 32)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.MiddleLeft, 1920, 1080, options), Is.EqualTo(new Point(32, 441)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.BottomLeft, 1920, 1080, options), Is.EqualTo(new Point(32, 1080 - 32 - 198)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.TopLeft, 1920, 1080, options, 3), Is.EqualTo(new Point(33, 33)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.MiddleLeft, 1920, 1080, options, 3), Is.EqualTo(new Point(33, 441)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.BottomLeft, 1920, 1080, options, 3), Is.EqualTo(new Point(33, 849)));
      Assert.That(Marker.RecommendedOrigin(MarkerSlot.TopLeft, 1920, 1080, options, 4), Is.EqualTo(new Point(32, 32)));
    }

    [Test]
    public void Symbols_FrameAndEndAreVersion2_StartGrowsWithTheName()
    {
      var generator = new MarkerGenerator();
      var matrix = new ModuleMatrix();
      Assert.That(generator.GenerateModules(new Payload(1, 2, 3), matrix), Is.True);
      Assert.That(matrix.Size, Is.EqualTo(25));
      Assert.That(generator.GenerateModules(new Payload(1, 2, 3, MarkerKind.SequenceEnd), matrix), Is.True);
      Assert.That(matrix.Size, Is.EqualTo(25));
      Assert.That(generator.GenerateModules(new Payload(1, 2, 3, MarkerKind.SequenceStart), default, matrix), Is.True);
      Assert.That(matrix.Size, Is.EqualTo(29), "33 bytes do not fit version 2-M");
      var maxName = new StartMetadata(123, new string('x', Marker.MaxStartNameBytes));
      Assert.That(generator.GenerateModules(new Payload(1, 2, 3, MarkerKind.SequenceStart), maxName, matrix), Is.True);
      Assert.That(matrix.Size, Is.EqualTo(Marker.MaxQrModuleCount));
      var tooLong = new StartMetadata(123, new string('x', Marker.MaxStartNameBytes + 1));
      Assert.That(generator.GenerateModules(new Payload(1, 2, 3, MarkerKind.SequenceStart), tooLong, matrix), Is.False);
    }

    [Test]
    public void Quads_ArePixelAligned_BackgroundFirst()
    {
      var quads = new Quad[Marker.MaxQuadCount];
      int count = new MarkerGenerator().GenerateQuads(new Payload(5, 6, 7), new Options(3, 4), new Point(10, 20), quads);
      Assert.That(count, Is.InRange(2, Marker.MaxFrameQuadCount));
      Assert.That(quads[0], Is.EqualTo(new Quad(10, 20, 10 + 99, 20 + 99, false)));
      foreach (var quad in quads.Skip(1).Take(count - 1))
      {
        Assert.That(quad.Dark, Is.True);
        Assert.That(quad.Height, Is.EqualTo(3));
        Assert.That((quad.Left - 22) % 3, Is.Zero, "left edges on module boundaries");
        Assert.That((quad.Top - 32) % 3, Is.Zero, "top edges on module boundaries");
      }
    }

    [Test]
    public void Triangles_AndIndexed_EqualTheConvertedQuads()
    {
      var generator = new MarkerGenerator();
      var payload = new Payload(42, 1_234_567, 3);
      var options = new Options(2, 4);
      var origin = new Point(7, 9);
      var quads = new Quad[Marker.MaxQuadCount];
      int quadCount = generator.GenerateQuads(payload, options, origin, quads);

      var converted = new Vertex[quadCount * 6];
      Assert.That(Marker.QuadsToTriangles(quads, quadCount, converted), Is.EqualTo(quadCount * 6));
      var direct = new Vertex[Marker.MaxFrameTriangleVertexCount];
      int vertexCount = generator.GenerateTriangles(payload, options, origin, direct);
      Assert.That(direct.Take(vertexCount), Is.EqualTo(converted));

      var convertedVertices = new Vertex[quadCount * 4];
      var convertedIndices = new int[quadCount * 6];
      Marker.QuadsToIndexed(quads, quadCount, convertedVertices, convertedIndices, 50);
      var vertices = new Vertex[Marker.MaxFrameIndexedVertexCount];
      var indices = new int[Marker.MaxFrameIndexCount];
      var count = generator.GenerateIndexed(payload, options, origin, vertices, indices, 50);
      Assert.That(vertices.Take(count.VertexCount), Is.EqualTo(convertedVertices));
      Assert.That(indices.Take(count.IndexCount), Is.EqualTo(convertedIndices));
    }

    [Test]
    public void VertexOrder_MatchesTheCppLibrary()
    {
      var quads = new[] { new Quad(0, 0, 10, 10, false), new Quad(2, 3, 4, 5, true) };
      var triangles = new Vertex[12];
      Assert.That(Marker.QuadsToTriangles(quads, 2, triangles), Is.EqualTo(12));
      Assert.That(
        triangles.Take(6),
        Is.EqualTo(
          new[]
          {
            new Vertex(0, 0, 255),
            new Vertex(10, 0, 255),
            new Vertex(0, 10, 255),
            new Vertex(0, 10, 255),
            new Vertex(10, 0, 255),
            new Vertex(10, 10, 255),
          }
        )
      );
      Assert.That(triangles[6], Is.EqualTo(new Vertex(2, 3, 0)));
      Assert.That(triangles[11], Is.EqualTo(new Vertex(4, 5, 0)));

      var vertices = new Vertex[8];
      var indices = new int[12];
      var count = Marker.QuadsToIndexed(quads, 2, vertices, indices, 100);
      Assert.That((count.VertexCount, count.IndexCount), Is.EqualTo((8, 12)));
      Assert.That(vertices.Skip(4), Is.EqualTo(new[] { new Vertex(2, 3, 0), new Vertex(4, 3, 0), new Vertex(4, 5, 0), new Vertex(2, 5, 0) }));
      Assert.That(indices, Is.EqualTo(new[] { 100, 101, 103, 103, 101, 102, 104, 105, 107, 107, 105, 106 }));
    }

    [Test]
    public void InvalidOptions_OrSmallBuffers_GenerateNothing()
    {
      var generator = new MarkerGenerator();
      var payload = new Payload(1, 2, 3);
      Assert.That(generator.GenerateQuads(payload, new Options(0), default, new Quad[Marker.MaxQuadCount]), Is.Zero);
      Assert.That(generator.GenerateTriangles(payload, new Options(6, -1), default, new Vertex[Marker.MaxTriangleVertexCount]), Is.Zero);
      Assert.That(generator.GenerateQuads(payload, new Options(6, Marker.MaxQuietZoneModules + 1), default, new Quad[Marker.MaxQuadCount]), Is.Zero);
      Assert.That(generator.GenerateQuads(payload, Options.Default, default, new Quad[10]), Is.Zero);
      Assert.That(generator.GenerateTriangles(payload, Options.Default, default, new Vertex[12]), Is.Zero);
      var failed = generator.GenerateIndexed(payload, Options.Default, default, new Vertex[Marker.MaxIndexedVertexCount], new int[12]);
      Assert.That((failed.VertexCount, failed.IndexCount), Is.EqualTo((0, 0)));
      Assert.That(Marker.QuadsToTriangles(new Quad[2], 2, new Vertex[11]), Is.Zero);
    }

    [Test]
    public void FrameMarkers_FitTheFrameBufferSizes()
    {
      var generator = new MarkerGenerator();
      var vertices = new Vertex[Marker.MaxFrameTriangleVertexCount];
      for (ulong frame = 0; frame < 500; ++frame)
        Assert.That(
          generator.GenerateTriangles(new Payload(frame * 7919, (long)frame * 166_667, 9), Options.Default, default, vertices),
          Is.Positive
        );
    }
  }
}
