//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The C# library must draw exactly what the C++ library draws: the same module matrix for 512 pseudo random payloads
//* (test-data/markers/modules.csv) and byte identical golden images from quads, triangle lists and indexed triangle lists.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  [TestFixture]
  public class CrossLanguageTests
  {
    private static IEnumerable<GoldenMarker> Golden() => GoldenData.Markers();

    [Test]
    public void ModuleMatrices_MatchTheCppLibrary()
    {
      var generator = new MarkerGenerator();
      var bits = new byte[Marker.MaxPackedModuleByteCount];
      var rows = GoldenData.ModuleDigest().ToList();
      Assert.That(rows, Has.Count.EqualTo(512));

      var mismatches = new List<string>();
      foreach (var row in rows)
      {
        if (!generator.TryGenerateModules(row.Payload, row.Start, bits, out var matrix))
        {
          mismatches.Add($"{row}: TryGenerateModules failed");
          continue;
        }
        // The matrix is stored packed exactly as the digest writes it; IsDark reads the same bits
        if (matrix.Size != row.Size || Hex(matrix.Bits) != row.ModulesHex || Pack(matrix) != row.ModulesHex)
          mismatches.Add($"{row}: {row.Payload} size {matrix.Size} (expected {row.Size})");
      }
      Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches.Take(10)));

      // Every main marker is version 6, every sync marker version 2
      Assert.That(rows.Select(r => r.Size).Distinct().OrderBy(s => s), Is.EqualTo(new[] { Marker.SyncQrModuleCount, Marker.QrModuleCount }));
      Assert.That(rows.Where(r => r.Payload.Kind == MarkerKind.Sync).Select(r => r.Size).Distinct(), Is.EqualTo(new[] { Marker.SyncQrModuleCount }));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromQuads(GoldenMarker golden)
    {
      var quads = new Quad[Marker.MaxQuadCount];
      int count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? TestMarkers.GenerateStartQuads(golden.Payload, golden.Start, golden.Options, golden.Origin, quads)
          : TestMarkers.GenerateQuads(golden.Payload, golden.Options, golden.Origin, quads);
      Assert.That(count, Is.GreaterThan(0));
      AssertMatchesGolden(golden, SoftwareRaster.Quads(quads.Take(count).ToList(), golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromTriangles(GoldenMarker golden)
    {
      var vertices = new Vertex[Marker.MaxTriangleVertexCount];
      int count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? TestMarkers.GenerateStartTriangles(golden.Payload, golden.Start, golden.Options, golden.Origin, vertices)
          : TestMarkers.GenerateTriangles(golden.Payload, golden.Options, golden.Origin, vertices);
      Assert.That(count, Is.GreaterThan(0));
      AssertMatchesGolden(golden, SoftwareRaster.Triangles(vertices.Take(count).ToList(), golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromIndexedTriangles(GoldenMarker golden)
    {
      const int BaseVertex = 100;
      var vertices = new Vertex[Marker.MaxIndexedVertexCount];
      var indices = new int[Marker.MaxIndexCount];
      var count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? TestMarkers.GenerateStartIndexed(golden.Payload, golden.Start, golden.Options, golden.Origin, vertices, indices, BaseVertex)
          : TestMarkers.GenerateIndexed(golden.Payload, golden.Options, golden.Origin, vertices, indices, BaseVertex);
      Assert.That(count.IndexCount, Is.GreaterThan(0));
      var expanded = SoftwareRaster.Expand(vertices, indices, count.IndexCount, BaseVertex);
      AssertMatchesGolden(golden, SoftwareRaster.Triangles(expanded, golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromTheGrid(GoldenMarker golden)
    {
      var kind = golden.Payload.Kind == MarkerKind.Sync ? MarkerKind.Sync : MarkerKind.Frame;
      var grid = new Vertex[Marker.MaxGridVertexCount];
      var indices = new int[Marker.MaxIndexCount];
      Assert.That(Marker.GridVertices(kind, golden.Options, golden.Origin, grid), Is.GreaterThan(0));
      int count = Marker.ModulesToGridIndices(TestMarkers.Encode(golden.Payload, golden.Start), indices);
      Assert.That(count, Is.GreaterThan(0));
      var expanded = SoftwareRaster.Expand(grid, indices, count, 0);
      AssertMatchesGolden(golden, SoftwareRaster.Triangles(expanded, golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromTheBitmap(GoldenMarker golden)
    {
      var pixels = new byte[golden.Width * golden.Height];
      Array.Fill(pixels, (byte)128);
      var matrix = TestMarkers.Encode(golden.Payload, golden.Start);
      Assert.That(Marker.ModulesToBitmap(matrix, golden.Options, golden.Origin, pixels, golden.Width, golden.Height, PixelFormat.Gray8), Is.True);
      AssertMatchesGolden(golden, pixels);
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static void AssertMatchesGolden(GoldenMarker golden, byte[] pixels)
    {
      var expected = GoldenData.ReadPgm(golden.File, out int width, out int height);
      Assert.That((width, height), Is.EqualTo((golden.Width, golden.Height)));
      int firstDifference = -1;
      for (int i = 0; i < expected.Length && firstDifference < 0; ++i)
      {
        if (pixels[i] != expected[i])
          firstDifference = i;
      }
      Assert.That(
        firstDifference,
        Is.EqualTo(-1),
        firstDifference < 0 ? "" : $"first difference at ({firstDifference % width}, {firstDifference / width}) in {golden.File}"
      );
    }

    /// <summary>The digest format: row major, one bit per module (1 = dark), most significant bit first, lower case hex.</summary>
    private static string Pack(ModuleMatrix matrix)
    {
      var hex = new StringBuilder();
      int current = 0;
      int bits = 0;
      for (int y = 0; y < matrix.Size; ++y)
      {
        for (int x = 0; x < matrix.Size; ++x)
        {
          current = (current << 1) | (matrix.IsDark(x, y) ? 1 : 0);
          if (++bits == 8)
          {
            hex.Append(current.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            current = 0;
            bits = 0;
          }
        }
      }
      if (bits > 0)
        hex.Append((current << (8 - bits)).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
      return hex.ToString();
    }
  }
}
