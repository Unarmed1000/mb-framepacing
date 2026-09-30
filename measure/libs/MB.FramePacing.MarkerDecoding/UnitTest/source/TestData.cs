//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Locates sdk/test-data/markers (golden images written by sdk/cpp/marker/tools/marker-render --golden) and parses its manifest.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
{
  public static class TestData
  {
    public static string MarkerDirectory => System.IO.Path.Combine(FindRepositoryRoot(), "sdk", "test-data", "markers");

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
          kind,
          uint.Parse(Field("runId"), CultureInfo.InvariantCulture),
          ulong.Parse(Field("frameIndex"), CultureInfo.InvariantCulture),
          (MB.FramePacing.Marker.MarkerFlags)byte.Parse(Field("flags"), CultureInfo.InvariantCulture),
          long.Parse(Field("animationTicks"), CultureInfo.InvariantCulture),
          PreferredFrameTicks: uint.Parse(Field("preferredFrameTicks"), CultureInfo.InvariantCulture),
          TargetFrameTicks: uint.Parse(Field("targetFrameTicks"), CultureInfo.InvariantCulture),
          IntendedDisplayTicks: long.Parse(Field("intendedDisplayTicks"), CultureInfo.InvariantCulture),
          CpuStartTicks: long.Parse(Field("cpuStartTicks"), CultureInfo.InvariantCulture),
          CpuBusyTicks: uint.Parse(Field("cpuBusyTicks"), CultureInfo.InvariantCulture)
        );
        StartMetadata? start = null;
        if (kind == MarkerKind.SequenceStart)
        {
          var sequenceId = MB.FramePacing.Marker.SequenceId.FromBytes(Convert.FromHexString(Field("sequenceIdHex")));
          start = new StartMetadata(long.Parse(Field("startUtcTicks"), CultureInfo.InvariantCulture), sequenceId);
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
        if (File.Exists(System.IO.Path.Combine(directory.FullName, "sdk", "test-data", "markers", "manifest.csv")))
          return directory.FullName;
        directory = directory.Parent;
      }
      throw new DirectoryNotFoundException("Could not locate sdk/test-data/markers/manifest.csv above " + TestContext.CurrentContext.TestDirectory);
    }
  }
}
