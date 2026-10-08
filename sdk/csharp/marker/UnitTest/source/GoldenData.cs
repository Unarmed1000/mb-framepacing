//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads the golden data the C++ library writes with marker-render --golden (test-data/markers): the image manifest, the module digest
//* and the PGM images.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  public static class GoldenData
  {
    public static string MarkerDirectory => Path.Combine(FindRepositoryRoot(), "test-data", "markers");

    public static IEnumerable<GoldenMarker> Markers()
    {
      foreach (var row in Rows("manifest.csv"))
      {
        yield return new GoldenMarker(
          row.Text("file"),
          Payload(row),
          Start(row),
          new Options(row.Int("moduleSizePx"), row.Int("quietZoneModules")),
          new Point(row.Int("originX"), row.Int("originY")),
          row.Int("width"),
          row.Int("height")
        );
      }
    }

    public static IEnumerable<ModuleDigestRow> ModuleDigest()
    {
      foreach (var row in Rows("modules.csv"))
        yield return new ModuleDigestRow(row.Line, Payload(row), Start(row), row.Int("size"), row.Text("modulesHex"));
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

    /// <summary>A CSV row of the golden data, its cells by column name.</summary>
    private sealed record Row(int Line, IReadOnlyDictionary<string, string> Cells)
    {
      public string Text(string column) => Cells[column];

      public int Int(string column) => int.Parse(Cells[column], CultureInfo.InvariantCulture);

      public long Long(string column) => long.Parse(Cells[column], CultureInfo.InvariantCulture);
    }

    private static IEnumerable<Row> Rows(string file)
    {
      var lines = File.ReadAllLines(Path.Combine(MarkerDirectory, file));
      var header = lines[0].Split(',');
      for (int i = 1; i < lines.Length; ++i)
      {
        var cells = lines[i].Split(',');
        yield return new Row(i + 1, header.Select((name, column) => (name, column)).ToDictionary(c => c.name, c => cells[c.column]));
      }
    }

    private static Payload Payload(Row row) =>
      new Payload(
        (MarkerKind)byte.Parse(row.Text("kind"), CultureInfo.InvariantCulture),
        uint.Parse(row.Text("runId"), CultureInfo.InvariantCulture),
        ulong.Parse(row.Text("frameIndex"), CultureInfo.InvariantCulture),
        (MarkerFlags)byte.Parse(row.Text("flags"), CultureInfo.InvariantCulture),
        new NanosecondTimeSpan(row.Long("animationNs")),
        // The three durations as a marker holds them, four unsigned bytes: a value that does not fit is a fault in the golden data
        preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(uint.Parse(row.Text("preferredFrameNs"), CultureInfo.InvariantCulture)),
        targetFrameTime: NanosecondTimeDuration.FromNanoseconds(uint.Parse(row.Text("targetFrameNs"), CultureInfo.InvariantCulture)),
        intendedDisplayTime: new NanosecondTickCount(row.Long("intendedDisplayNs")),
        cpuStartTime: new NanosecondTickCount(row.Long("cpuStartNs")),
        cpuBusy: NanosecondTimeDuration.FromNanoseconds(uint.Parse(row.Text("cpuBusyNs"), CultureInfo.InvariantCulture))
      );

    private static StartMetadata Start(Row row) =>
      new StartMetadata(
        row.Long("startUtcTicks"),
        row.Text("sequenceIdHex") is { Length: > 0 } hex ? SequenceId.FromBytes(Convert.FromHexString(hex)) : default
      );

    private static string FindRepositoryRoot()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "markers", "manifest.csv")))
        directory = directory.Parent;
      return directory?.FullName ?? throw new DirectoryNotFoundException("test-data/markers not found above the test directory");
    }
  }
}
