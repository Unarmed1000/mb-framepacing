//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Locates test-data/markers (golden images written by marker/cpp/tools/marker-render --golden) and parses its manifest.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  public sealed record GoldenMarker(
    string Path,
    MarkerPayload Payload,
    StartMetadata? Start,
    int ModuleSizePx,
    int QuietZoneModules,
    int OriginX,
    int OriginY
  )
  {
    public override string ToString() => System.IO.Path.GetFileName(Path);
  }

  public static class TestData
  {
    public static string MarkerDirectory => System.IO.Path.Combine(FindRepositoryRoot(), "test-data", "markers");

    public static IReadOnlyList<GoldenMarker> LoadGoldenMarkers()
    {
      var directory = MarkerDirectory;
      var lines = File.ReadAllLines(System.IO.Path.Combine(directory, "manifest.csv"));
      var header = lines[0].Split(',');
      var result = new List<GoldenMarker>();
      for (int i = 1; i < lines.Length; ++i)
      {
        if (string.IsNullOrWhiteSpace(lines[i]))
          continue;
        var fields = lines[i].Split(',');
        string Field(string name) => fields[Array.IndexOf(header, name)];

        var kind = (MarkerKind)byte.Parse(Field("kind"), CultureInfo.InvariantCulture);
        var payload = new MarkerPayload(
          ulong.Parse(Field("frameIndex"), CultureInfo.InvariantCulture),
          long.Parse(Field("animationTicks"), CultureInfo.InvariantCulture),
          uint.Parse(Field("runId"), CultureInfo.InvariantCulture),
          kind
        );
        StartMetadata? start = null;
        if (kind == MarkerKind.SequenceStart)
        {
          var name = System.Text.Encoding.UTF8.GetString(Convert.FromHexString(Field("startNameHex")));
          start = new StartMetadata(long.Parse(Field("startUtcTicks"), CultureInfo.InvariantCulture), name);
        }
        result.Add(
          new GoldenMarker(
            System.IO.Path.Combine(directory, Field("file")),
            payload,
            start,
            int.Parse(Field("moduleSizePx"), CultureInfo.InvariantCulture),
            int.Parse(Field("quietZoneModules"), CultureInfo.InvariantCulture),
            int.Parse(Field("originX"), CultureInfo.InvariantCulture),
            int.Parse(Field("originY"), CultureInfo.InvariantCulture)
          )
        );
      }
      return result;
    }

    private static string FindRepositoryRoot()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null)
      {
        if (File.Exists(System.IO.Path.Combine(directory.FullName, "test-data", "markers", "manifest.csv")))
          return directory.FullName;
        directory = directory.Parent;
      }
      throw new DirectoryNotFoundException("Could not locate test-data/markers/manifest.csv above " + TestContext.CurrentContext.TestDirectory);
    }
  }
}
