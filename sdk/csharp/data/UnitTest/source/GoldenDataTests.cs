//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The golden data (test-data/data): the library reads what digest.json says it holds, and writes every file back as it was (apart from
//* line endings, which the tools write as the platform's). MB_FRAMEPACING_UPDATE_TEST_DATA=1 writes digest.json instead of checking it
//* (tools/update_test_data.py).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace MB.FramePacing.Data.UnitTest
{
  [TestFixture]
  public class GoldenDataTests
  {
    private static readonly JsonSerializerOptions g_digestOptions = new JsonSerializerOptions { WriteIndented = true };

    [Test]
    public void Digest_MatchesTheGoldenData()
    {
      string digest = DataDigest.Compute(TestData.ClipDirectory).ToJsonString(g_digestOptions).Replace("\r\n", "\n") + "\n";
      if (Environment.GetEnvironmentVariable("MB_FRAMEPACING_UPDATE_TEST_DATA") == "1")
      {
        File.WriteAllText(TestData.DigestPath, digest.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
        Assert.Pass("digest.json written");
      }
      Assert.That(digest, Is.EqualTo(TestData.ReadText(TestData.DigestPath)));
    }

    [Test]
    public void CaptureData_WritesBackByteForByte()
    {
      string path = Path.Combine(TestData.ClipDirectory, CaptureDataHeader.FileName);
      string copy = Path.Combine(Path.GetTempPath(), $"mb-framepacing-data-{Guid.NewGuid():N}.mbcd");
      try
      {
        using (var reader = new CaptureDataReader(path))
        using (var writer = new CaptureDataWriter(copy, reader.Header))
          writer.WriteRecords(reader.ReadAll());
        Assert.That(File.ReadAllBytes(copy), Is.EqualTo(File.ReadAllBytes(path)));
      }
      finally
      {
        File.Delete(copy);
      }
    }

    [Test]
    public void Summary_WritesBackAsItWas()
    {
      string path = Path.Combine(TestData.AnalysisDirectory, AnalysisFiles.SummaryFileName);
      var summary = AnalysisSummary.Read(path);
      Assert.That(summary.FormatVersion, Is.EqualTo(AnalysisSummary.CurrentFormatVersion));
      Assert.That(summary.ToJson().Replace("\r\n", "\n"), Is.EqualTo(TestData.ReadText(path)));
    }

    [Test]
    public void FramesCsv_WritesBackAsItWas()
    {
      var summary = AnalysisSummary.Read(Path.Combine(TestData.AnalysisDirectory, AnalysisFiles.SummaryFileName));
      bool camera = summary.Scanout == "Camera";
      foreach (var run in summary.Runs)
      {
        string path = Path.Combine(TestData.AnalysisDirectory, run.FramesFile);
        using var writer = new StringWriter { NewLine = "\n" };
        FramesCsv.Write(writer, FramesCsv.Read(path), camera);
        Assert.That(writer.ToString(), Is.EqualTo(TestData.ReadText(path)), run.FramesFile);
      }
    }

    [Test]
    public void CapturesCsv_WritesBackAsItWas()
    {
      string path = Path.Combine(TestData.AnalysisDirectory, AnalysisFiles.CapturesFileName);
      using var writer = new StringWriter { NewLine = "\n" };
      CapturesCsv.Write(writer, CapturesCsv.Read(path));
      Assert.That(writer.ToString(), Is.EqualTo(TestData.ReadText(path)));
    }

    [Test]
    public void Records_DecodeThroughTheMarkerLibrary()
    {
      using var reader = new CaptureDataReader(Path.Combine(TestData.ClipDirectory, CaptureDataHeader.FileName));
      var records = reader.ReadAll();
      var decoded = records.Where(r => r.CaptureStatus == CaptureDataStatus.Decoded).ToList();
      Assert.That(decoded, Is.Not.Empty);
      Assert.That(decoded.All(r => r.TryDecodeMain(out _, out _)), "every decoded record carries a valid main payload");
      Assert.That(records.Where(r => r.MainBytes == null).All(r => !r.TryDecodeMain(out _, out _)));
    }
  }
}
