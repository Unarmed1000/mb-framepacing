//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads the golden data the C++ library writes with marker-render --golden (test-data/markers): the image manifest, the module digest
//* and the PGM images.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  public static class GoldenData
  {
    public static string MarkerDirectory => Path.Combine(FindRepositoryRoot(), "test-data", "markers");

    public static IEnumerable<GoldenMarker> Markers()
    {
      foreach (var line in File.ReadLines(Path.Combine(MarkerDirectory, "manifest.csv")).Skip(1))
      {
        var f = line.Split(',');
        var payload = new Payload(
          ulong.Parse(f[3], CultureInfo.InvariantCulture),
          long.Parse(f[4], CultureInfo.InvariantCulture),
          uint.Parse(f[2], CultureInfo.InvariantCulture),
          (MarkerKind)byte.Parse(f[1], CultureInfo.InvariantCulture)
        );
        yield return new GoldenMarker(
          f[0],
          payload,
          new StartMetadata(long.Parse(f[5], CultureInfo.InvariantCulture), FromHex(f[6])),
          new Options(int.Parse(f[7], CultureInfo.InvariantCulture), int.Parse(f[8], CultureInfo.InvariantCulture)),
          new Point(int.Parse(f[9], CultureInfo.InvariantCulture), int.Parse(f[10], CultureInfo.InvariantCulture)),
          int.Parse(f[11], CultureInfo.InvariantCulture),
          int.Parse(f[12], CultureInfo.InvariantCulture)
        );
      }
    }

    public static IEnumerable<ModuleDigestRow> ModuleDigest()
    {
      int line = 1;
      foreach (var text in File.ReadLines(Path.Combine(MarkerDirectory, "modules.csv")).Skip(1))
      {
        ++line;
        var f = text.Split(',');
        var payload = new Payload(
          ulong.Parse(f[2], CultureInfo.InvariantCulture),
          long.Parse(f[3], CultureInfo.InvariantCulture),
          uint.Parse(f[1], CultureInfo.InvariantCulture),
          (MarkerKind)byte.Parse(f[0], CultureInfo.InvariantCulture)
        );
        yield return new ModuleDigestRow(
          line,
          payload,
          new StartMetadata(long.Parse(f[4], CultureInfo.InvariantCulture), FromHex(f[5])),
          int.Parse(f[6], CultureInfo.InvariantCulture),
          f[7]
        );
      }
    }

    /// <summary>Read a binary PGM (P5, 8 bit): the pixel bytes.</summary>
    public static byte[] ReadPgm(string file, out int width, out int height)
    {
      var bytes = File.ReadAllBytes(Path.Combine(MarkerDirectory, file));
      int position = 0;
      string NextToken()
      {
        while (char.IsWhiteSpace((char)bytes[position]))
          ++position;
        int start = position;
        while (!char.IsWhiteSpace((char)bytes[position]))
          ++position;
        return Encoding.ASCII.GetString(bytes, start, position - start);
      }
      Assert.That(NextToken(), Is.EqualTo("P5"), file);
      width = int.Parse(NextToken(), CultureInfo.InvariantCulture);
      height = int.Parse(NextToken(), CultureInfo.InvariantCulture);
      Assert.That(NextToken(), Is.EqualTo("255"), file);
      ++position; // the single whitespace after the maximum value
      return bytes.AsSpan(position, width * height).ToArray();
    }

    private static string FromHex(string hex) => Encoding.UTF8.GetString(Convert.FromHexString(hex));

    private static string FindRepositoryRoot()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "markers", "manifest.csv")))
        directory = directory.Parent;
      return directory?.FullName ?? throw new DirectoryNotFoundException("test-data/markers not found above the test directory");
    }
  }
}
