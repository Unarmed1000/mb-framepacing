//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sizes, placement, symbol versions, the vertex order of every output and the failure cases. The values match the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
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
      Assert.That(Marker.MaxQuadCount, Is.EqualTo(862));
      Assert.That(Marker.MaxTriangleVertexCount, Is.EqualTo(862 * 6));
      Assert.That(Marker.MaxIndexCount, Is.EqualTo(862 * 6));
      Assert.That(Marker.MaxEncodedPayloadByteCount, Is.EqualTo(77));
      Assert.That(Marker.MaxEncodedPayloadByteCount, Is.LessThanOrEqualTo(Marker.QrCapacityBytes));
    }

    [Test]
    public void MarkerSize()
    {
      Assert.That(Marker.MarkerSizePx(Options.Default), Is.EqualTo(294));
      Assert.That(Marker.MarkerSizePx(Options.Default, MarkerKind.Sync), Is.EqualTo(198));
      Assert.That(Marker.MarkerSizePx(new Options(3)), Is.EqualTo(147));
      Assert.That(Marker.MarkerSizePx(new Options(12)), Is.EqualTo(588));
      Assert.That(Marker.MarkerSizePx(new Options(1, 0)), Is.EqualTo(41));
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
      Assert.That(Marker.RecommendedOrigin(MarkerKind.Frame, 1920, 1080, options), Is.EqualTo(new Point(32, 32)));
      Assert.That(Marker.RecommendedOrigin(MarkerKind.SequenceStart, 1920, 1080, options), Is.EqualTo(new Point(32, 32)));
      Assert.That(Marker.RecommendedOrigin(MarkerKind.Sync, 1920, 1080, options), Is.EqualTo(new Point(32, 1080 - 32 - 198)));
      Assert.That(Marker.RecommendedOrigin(MarkerKind.Frame, 1920, 1080, options, 3), Is.EqualTo(new Point(33, 33)));
      Assert.That(Marker.RecommendedOrigin(MarkerKind.Sync, 1920, 1080, options, 3), Is.EqualTo(new Point(33, 849)));
      Assert.That(Marker.RecommendedOrigin(MarkerKind.Frame, 1920, 1080, options, 4), Is.EqualTo(new Point(32, 32)));
    }

    [Test]
    public void Symbols_SyncMarkersAreVersion2()
    {
      var generator = new MarkerGenerator();
      Span<byte> bits = stackalloc byte[Marker.MaxPackedModuleByteCount];
      Assert.That(
        generator.TryGenerateModules(
          new Payload(MarkerKind.Sync, 3, 1, MarkerFlags.None, 2, targetFrameTicks: 5, intendedDisplayTicks: 4),
          bits,
          out var matrix
        ),
        Is.True
      );
      Assert.That(matrix.Size, Is.EqualTo(Marker.SyncQrModuleCount));
      Assert.That(matrix.Bits.Length, Is.EqualTo(79));
      var quads = new Quad[Marker.MaxQuadCount];
      int count = TestMarkers.GenerateQuads(new Payload(MarkerKind.Sync, 0, 7, MarkerFlags.None, 0), new Options(3, 4), new Point(10, 20), quads);
      Assert.That(count, Is.GreaterThan(1));
      Assert.That(quads[0], Is.EqualTo(new Quad(10, 20, 10 + 99, 20 + 99, false)));
    }

    [Test]
    public void Symbols_EveryMarkerIsVersion6()
    {
      var generator = new MarkerGenerator();
      var bits = new byte[Marker.MaxPackedModuleByteCount];
      Assert.That(
        generator.TryGenerateModules(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2), bits, out var frame) && frame.Size == 41,
        Is.True
      );
      Assert.That(
        generator.TryGenerateModules(new Payload(MarkerKind.SequenceEnd, 3, 1, MarkerFlags.None, 2), bits, out var end) && end.Size == 41,
        Is.True
      );
      Assert.That(
        generator.TryGenerateModules(new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.None, 2), default, bits, out var start)
          && start.Size == 41,
        Is.True
      );
      var full = new StartMetadata(123, new SequenceId(ulong.MaxValue, ulong.MaxValue));
      Assert.That(
        generator.TryGenerateModules(new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.None, 2), full, bits, out var matrix),
        Is.True
      );
      Assert.That(matrix.Size, Is.EqualTo(Marker.QrModuleCount));
      Assert.That(matrix.Bits.Length, Is.EqualTo(Marker.MaxPackedModuleByteCount));
    }

    [Test]
    public void Quads_ArePixelAligned_BackgroundFirst()
    {
      var quads = new Quad[Marker.MaxQuadCount];
      int count = TestMarkers.GenerateQuads(new Payload(MarkerKind.Frame, 7, 5, MarkerFlags.None, 6), new Options(3, 4), new Point(10, 20), quads);
      Assert.That(count, Is.InRange(2, Marker.MaxQuadCount));
      Assert.That(quads[0], Is.EqualTo(new Quad(10, 20, 10 + 147, 20 + 147, false)));
      foreach (var quad in quads.Skip(1).Take(count - 1))
      {
        Assert.That(quad.Dark, Is.True);
        Assert.That(quad.Height, Is.EqualTo(3));
        Assert.That((quad.Left - 22) % 3, Is.Zero, "left edges on module boundaries");
        Assert.That((quad.Top - 32) % 3, Is.Zero, "top edges on module boundaries");
      }
    }

    [Test]
    public void Triangles_AndIndexed_FollowTheQuadsInTheDocumentedOrder()
    {
      var payload = new Payload(MarkerKind.Frame, 3, 42, MarkerFlags.None, 1_234_567);
      var options = new Options(2, 4);
      var origin = new Point(7, 9);
      var quads = new Quad[Marker.MaxQuadCount];
      int quadCount = TestMarkers.GenerateQuads(payload, options, origin, quads);

      var converted = TestMarkers.ToTriangles(quads.Take(quadCount));
      var direct = new Vertex[Marker.MaxTriangleVertexCount];
      int vertexCount = TestMarkers.GenerateTriangles(payload, options, origin, direct);
      Assert.That(direct.Take(vertexCount), Is.EqualTo(converted));

      var (convertedVertices, convertedIndices) = TestMarkers.ToIndexed(quads.Take(quadCount), 50);
      var vertices = new Vertex[Marker.MaxIndexedVertexCount];
      var indices = new int[Marker.MaxIndexCount];
      var count = TestMarkers.GenerateIndexed(payload, options, origin, vertices, indices, 50);
      Assert.That(vertices.Take(count.VertexCount), Is.EqualTo(convertedVertices));
      Assert.That(indices.Take(count.IndexCount), Is.EqualTo(convertedIndices));
    }

    [Test]
    public void VertexOrder_MatchesTheCppLibrary()
    {
      // The background quad comes first: its vertices in the documented order
      var matrix = TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2));
      var options = new Options(2, 4);
      int size = Marker.MarkerSizePx(options);
      var triangles = new Vertex[Marker.MaxTriangleVertexCount];
      Assert.That(Marker.ModulesToTriangles(matrix, options, new Point(10, 20), triangles), Is.GreaterThan(6));
      Assert.That(
        triangles.Take(6),
        Is.EqualTo(
          new[]
          {
            new Vertex(10, 20, 255),
            new Vertex(10 + size, 20, 255),
            new Vertex(10, 20 + size, 255),
            new Vertex(10, 20 + size, 255),
            new Vertex(10 + size, 20, 255),
            new Vertex(10 + size, 20 + size, 255),
          }
        )
      );

      var vertices = new Vertex[Marker.MaxIndexedVertexCount];
      var indices = new int[Marker.MaxIndexCount];
      Assert.That(
        Marker
          .ModulesToIndexed(
            TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
            options,
            new Point(10, 20),
            vertices,
            indices,
            100
          )
          .IndexCount,
        Is.GreaterThan(6)
      );
      Assert.That(
        vertices.Take(4),
        Is.EqualTo(
          new[] { new Vertex(10, 20, 255), new Vertex(10 + size, 20, 255), new Vertex(10 + size, 20 + size, 255), new Vertex(10, 20 + size, 255) }
        )
      );
      Assert.That(indices.Take(12), Is.EqualTo(new[] { 100, 101, 103, 103, 101, 102, 104, 105, 107, 107, 105, 106 }));
    }

    [Test]
    public void InvalidOptions_OrSmallBuffers_GenerateNothing()
    {
      var payload = new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2);
      Assert.That(TestMarkers.GenerateQuads(payload, new Options(0), default, new Quad[Marker.MaxQuadCount]), Is.Zero);
      Assert.That(TestMarkers.GenerateTriangles(payload, new Options(6, -1), default, new Vertex[Marker.MaxTriangleVertexCount]), Is.Zero);
      Assert.That(
        TestMarkers.GenerateQuads(payload, new Options(6, Marker.MaxQuietZoneModules + 1), default, new Quad[Marker.MaxQuadCount]),
        Is.Zero
      );
      Assert.That(TestMarkers.GenerateQuads(payload, Options.Default, default, new Quad[10]), Is.Zero);
      Assert.That(TestMarkers.GenerateTriangles(payload, Options.Default, default, new Vertex[12]), Is.Zero);
      var failed = TestMarkers.GenerateIndexed(payload, Options.Default, default, new Vertex[Marker.MaxIndexedVertexCount], new int[12]);
      Assert.That((failed.VertexCount, failed.IndexCount), Is.EqualTo((0, 0)));
    }

    [Test]
    public void FrameMarkers_FitTheFrameBufferSizes()
    {
      var vertices = new Vertex[Marker.MaxTriangleVertexCount];
      for (ulong frame = 0; frame < 500; ++frame)
        Assert.That(
          TestMarkers.GenerateTriangles(
            new Payload(MarkerKind.Frame, 9, frame * 7919, MarkerFlags.None, (long)frame * 166_667),
            Options.Default,
            default,
            vertices
          ),
          Is.Positive
        );
    }

    [Test]
    public void ModuleMatrix_IsPackedRowMajorMostSignificantBitFirst()
    {
      var generator = new MarkerGenerator();
      var bits = new byte[Marker.MaxPackedModuleByteCount];
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.Sync })
      {
        Assert.That(generator.TryGenerateModules(new Payload(kind, 9, 12345, MarkerFlags.None, 678), bits, out var matrix), Is.True);
        Assert.That(matrix.Bits.Length, Is.EqualTo(Marker.PackedModuleByteCount(matrix.Size)));
        for (int y = 0; y < matrix.Size; ++y)
        {
          for (int x = 0; x < matrix.Size; ++x)
          {
            int index = (y * matrix.Size) + x;
            Assert.That(matrix.IsDark(x, y), Is.EqualTo(((bits[index / 8] >> (7 - (index % 8))) & 1) == 1), $"{x},{y}");
          }
        }
      }
      Assert.That(Marker.PackedModuleByteCount(Marker.QrModuleCount), Is.EqualTo(211));
      Assert.That(Marker.PackedModuleByteCount(Marker.SyncQrModuleCount), Is.EqualTo(79));
    }

    [Test]
    public void ModuleMatrix_TryFromBits_TakesQrSizesAndIgnoresThePadding()
    {
      var bits = new byte[Marker.MaxPackedModuleByteCount];
      Assert.That(new MarkerGenerator().TryGenerateModules(new Payload(MarkerKind.Sync, 3, 1, MarkerFlags.None, 2), bits, out var matrix), Is.True);
      var copy = (byte[])bits.Clone();
      copy[78] |= 0x7F; // 625 modules: the last byte uses 1 bit
      Assert.That(ModuleMatrix.TryFromBits(25, copy, out var fromBits), Is.True);
      Assert.That(fromBits.SequenceEqual(matrix), Is.True);
      Assert.That(ModuleMatrix.TryFromBits(24, copy, out _), Is.False);
      Assert.That(ModuleMatrix.TryFromBits(45, copy, out _), Is.False);
      Assert.That(ModuleMatrix.TryFromBits(25, copy.AsSpan(0, 78), out _), Is.False);
      Assert.That(
        new MarkerGenerator().TryGenerateModules(new Payload(MarkerKind.Sync, 3, 1, MarkerFlags.None, 2), new byte[78], out _),
        Is.False,
        "too small"
      );
    }

    [Test]
    public void EmptyMatrix_DrawsNothing()
    {
      Assert.That(Marker.ModulesToQuads(default, Options.Default, default, new Quad[4]), Is.Zero);
      Assert.That(Marker.ModulesToBitmap(default, new Options(1, 0), default, new byte[16], 4, 4, PixelFormat.Gray8), Is.False);
    }

    [TestCase(PixelFormat.Gray8)]
    [TestCase(PixelFormat.Rgb24)]
    [TestCase(PixelFormat.Rgba32)]
    public void Bitmap_EqualsTheRasterizedQuads(PixelFormat format)
    {
      const int Width = 173;
      const int Height = 131;
      var cases = new[]
      {
        (new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2), new Options(3, 4), new Point(5, 7)),
        (new Payload(MarkerKind.SequenceEnd, 1, 99, MarkerFlags.None, -5), new Options(1, 0), new Point(0, 0)),
        (new Payload(MarkerKind.Sync, 0, 7, MarkerFlags.None, 0), new Options(2, 4), new Point(3, 1)),
        (
          new Payload(
            MarkerKind.Frame,
            2,
            ulong.MaxValue,
            MarkerFlags.None,
            1,
            targetFrameTicks: 4,
            intendedDisplayTicks: 3,
            cpuStartTicks: 5,
            cpuBusyTicks: 6
          ),
          new Options(2, 1),
          new Point(-9, -4)
        ),
        (new Payload(MarkerKind.Frame, 7, 5, MarkerFlags.None, 6), new Options(4, 2), new Point(100, 60)),
      };
      int bytesPerPixel = Marker.BytesPerPixel(format);
      int stride = (Width * bytesPerPixel) + 5; // padded rows
      foreach (var (payload, options, origin) in cases)
      {
        var quads = new Quad[Marker.MaxQuadCount];
        var matrix = TestMarkers.Encode(payload);
        int count = Marker.ModulesToQuads(matrix, options, origin, quads);
        var expected = SoftwareRaster.Quads(quads.Take(count).ToList(), Width, Height);
        var pixels = new byte[stride * Height];
        Array.Fill(pixels, (byte)128);
        Assert.That(Marker.ModulesToBitmap(TestMarkers.Encode(payload), options, origin, pixels, Width, Height, format, stride), Is.True);
        for (int y = 0; y < Height; ++y)
        {
          for (int x = 0; x < Width; ++x)
          {
            byte luma = expected[(y * Width) + x];
            for (int channel = 0; channel < bytesPerPixel; ++channel)
            {
              byte wanted = channel == 3 && luma != 128 ? (byte)255 : luma;
              Assert.That(pixels[(y * stride) + (x * bytesPerPixel) + channel], Is.EqualTo(wanted), $"{payload}: pixel {x},{y} channel {channel}");
            }
          }
          for (int pad = Width * bytesPerPixel; pad < stride; ++pad)
            Assert.That(pixels[(y * stride) + pad], Is.EqualTo(128), "the row padding is never written");
        }
      }
    }

    [Test]
    public void Bitmap_ModuleResolutionScaledUp_EqualsTheFullSizeOne()
    {
      const int ModuleSize = 3;
      var small = new Options(1, 4);
      var large = new Options(ModuleSize, 4);
      int smallSize = Marker.MarkerSizePx(small);
      int largeSize = Marker.MarkerSizePx(large);
      var modules = new byte[smallSize * smallSize];
      var pixels = new byte[largeSize * largeSize];
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 59, 31, MarkerFlags.None, 41)),
          small,
          default,
          modules,
          smallSize,
          smallSize,
          PixelFormat.Gray8
        )
      );
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 59, 31, MarkerFlags.None, 41)),
          large,
          default,
          pixels,
          largeSize,
          largeSize,
          PixelFormat.Gray8
        )
      );
      for (int y = 0; y < largeSize; ++y)
      {
        for (int x = 0; x < largeSize; ++x)
          Assert.That(pixels[(y * largeSize) + x], Is.EqualTo(modules[(y / ModuleSize * smallSize) + (x / ModuleSize)]), $"{x},{y}");
      }
    }

    [Test]
    public void Bitmap_RefusesInvalidArguments_WithoutWriting()
    {
      var pixels = new byte[64 * 64 * 4];
      Array.Fill(pixels, (byte)128);
      var options = new Options(1, 4);
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
          new Options(0),
          default,
          pixels,
          64,
          64,
          PixelFormat.Gray8
        ),
        Is.False
      );
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
          options,
          default,
          pixels,
          64,
          64,
          PixelFormat.Rgb24,
          (64 * 3) - 1
        ),
        Is.False
      );
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
          options,
          default,
          pixels.AsSpan(0, pixels.Length - 1),
          64,
          64,
          PixelFormat.Rgba32
        ),
        Is.False
      );
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
          options,
          default,
          pixels,
          -1,
          64,
          PixelFormat.Gray8
        ),
        Is.False
      );
      Assert.That(pixels.All(p => p == 128), Is.True);
      Assert.That(
        Marker.ModulesToBitmap(
          TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)),
          options,
          new Point(500, 500),
          pixels,
          64,
          64,
          PixelFormat.Gray8
        ),
        Is.True
      );
      Assert.That(pixels.All(p => p == 128), Is.True, "outside the buffer: nothing to draw");
      Assert.That(
        (Marker.BytesPerPixel(PixelFormat.Gray8), Marker.BytesPerPixel(PixelFormat.Rgb24), Marker.BytesPerPixel(PixelFormat.Rgba32)),
        Is.EqualTo((1, 3, 4))
      );
    }

    [Test]
    public void Grid_VertexCountsFit16BitIndices()
    {
      Assert.That(Marker.GridVertexCount(MarkerKind.Frame), Is.EqualTo(1768));
      Assert.That(Marker.GridVertexCount(MarkerKind.Sync), Is.EqualTo(680));
      Assert.That(Marker.MaxGridVertexCount, Is.EqualTo(1768));
    }

    [Test]
    public void Grid_ResolvedIndices_EqualTheIndexedTrianglesTriangleByTriangle()
    {
      const int BaseVertex = 100;
      var payloads = new[]
      {
        new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2),
        new Payload(MarkerKind.SequenceEnd, 1, 99, MarkerFlags.None, -5),
        new Payload(MarkerKind.Sync, 0, 7, MarkerFlags.None, 0),
        new Payload(
          MarkerKind.Frame,
          2,
          ulong.MaxValue,
          MarkerFlags.None,
          1,
          targetFrameTicks: 4,
          intendedDisplayTicks: 3,
          cpuStartTicks: 5,
          cpuBusyTicks: 6
        ),
        new Payload(MarkerKind.SequenceStart, 7, 5, MarkerFlags.None, 6),
      };
      foreach (var payload in payloads)
      {
        foreach (var options in new[] { new Options(1, 0), new Options(3, 4), new Options(6, 2) })
        {
          var origin = new Point(17, 23);
          var grid = new Vertex[Marker.MaxGridVertexCount];
          Assert.That(Marker.GridVertices(payload.Kind, options, origin, grid), Is.EqualTo(Marker.GridVertexCount(payload.Kind)));
          var gridIndices = new int[Marker.MaxIndexCount];
          int gridCount = Marker.ModulesToGridIndices(TestMarkers.Encode(payload), gridIndices, BaseVertex);
          var vertices = new Vertex[Marker.MaxIndexedVertexCount];
          var indices = new int[Marker.MaxIndexCount];
          var count = Marker.ModulesToIndexed(TestMarkers.Encode(payload), options, origin, vertices, indices, BaseVertex);
          Assert.That(gridCount, Is.EqualTo(count.IndexCount), $"{payload}");
          for (int i = 0; i < gridCount; ++i)
            Assert.That(
              grid[gridIndices[i] - BaseVertex],
              Is.EqualTo(vertices[indices[i] - BaseVertex]),
              $"{payload}, module {options.ModuleSizePx}: index {i}"
            );
        }
      }
    }

    [Test]
    public void Grid_InvalidOptionsOrSmallBuffers_GiveNothing()
    {
      Assert.That(Marker.GridVertices(MarkerKind.Frame, new Options(0), default, new Vertex[Marker.MaxGridVertexCount]), Is.Zero);
      Assert.That(Marker.GridVertices(MarkerKind.Frame, Options.Default, default, new Vertex[1767]), Is.Zero);
      Assert.That(Marker.GridVertices(MarkerKind.Sync, Options.Default, default, new Vertex[680]), Is.EqualTo(680));
      Assert.That(Marker.ModulesToGridIndices(TestMarkers.Encode(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.None, 2)), new int[12]), Is.Zero);
      Assert.That(Marker.ModulesToGridIndices(default, new int[12]), Is.Zero);
    }
  }
}
