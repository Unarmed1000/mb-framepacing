//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The C# library must draw exactly what the C++ library draws: the same module matrix for 512 pseudo random payloads
//* (test-data/markers/modules.csv) and byte identical golden images from quads, triangle lists and indexed triangle lists.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

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
      var matrix = new ModuleMatrix();
      var rows = GoldenData.ModuleDigest().ToList();
      Assert.That(rows, Has.Count.EqualTo(512));

      var mismatches = new List<string>();
      foreach (var row in rows)
      {
        if (!generator.GenerateModules(row.Payload, row.Start, matrix))
        {
          mismatches.Add($"{row}: GenerateModules failed");
          continue;
        }
        if (matrix.Size != row.Size || Pack(matrix) != row.ModulesHex)
          mismatches.Add($"{row}: {row.Payload} size {matrix.Size} (expected {row.Size})");
      }
      Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches.Take(10)));

      // Every start marker version occurs, so the version choice is covered too
      Assert.That(rows.Select(r => r.Size).Distinct().OrderBy(s => s), Is.EqualTo(new[] { 25, 29, 33, 37, 41 }));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromQuads(GoldenMarker golden)
    {
      var quads = new Quad[Marker.MaxQuadCount];
      var generator = new MarkerGenerator();
      int count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? generator.GenerateStartQuads(golden.Payload, golden.Start, golden.Options, golden.Origin, quads)
          : generator.GenerateQuads(golden.Payload, golden.Options, golden.Origin, quads);
      Assert.That(count, Is.GreaterThan(0));
      AssertMatchesGolden(golden, SoftwareRaster.Quads(quads.Take(count).ToList(), golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromTriangles(GoldenMarker golden)
    {
      var vertices = new Vertex[Marker.MaxTriangleVertexCount];
      var generator = new MarkerGenerator();
      int count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? generator.GenerateStartTriangles(golden.Payload, golden.Start, golden.Options, golden.Origin, vertices)
          : generator.GenerateTriangles(golden.Payload, golden.Options, golden.Origin, vertices);
      Assert.That(count, Is.GreaterThan(0));
      AssertMatchesGolden(golden, SoftwareRaster.Triangles(vertices.Take(count).ToList(), golden.Width, golden.Height));
    }

    [TestCaseSource(nameof(Golden))]
    public void GoldenImage_FromIndexedTriangles(GoldenMarker golden)
    {
      const int BaseVertex = 100;
      var vertices = new Vertex[Marker.MaxIndexedVertexCount];
      var indices = new int[Marker.MaxIndexCount];
      var generator = new MarkerGenerator();
      var count =
        golden.Payload.Kind == MarkerKind.SequenceStart
          ? generator.GenerateStartIndexed(golden.Payload, golden.Start, golden.Options, golden.Origin, vertices, indices, BaseVertex)
          : generator.GenerateIndexed(golden.Payload, golden.Options, golden.Origin, vertices, indices, BaseVertex);
      Assert.That(count.IndexCount, Is.GreaterThan(0));
      var expanded = SoftwareRaster.Expand(vertices, indices, count.IndexCount, BaseVertex);
      AssertMatchesGolden(golden, SoftwareRaster.Triangles(expanded, golden.Width, golden.Height));
    }

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
