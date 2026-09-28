//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json and the CSVs: the format version, the file names, and columns an older file lacks or a newer one adds.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.IO;
using NUnit.Framework;

namespace MB.FramePacing.Data.UnitTest
{
  [TestFixture]
  public class AnalysisOutputTests
  {
    private const string MinimalSummary = """
      {
        "scanout": "SingleScanout",
        "analysedUtc": "2026-01-01T00:00:00Z",
        "capturePeriodMs": 16.6667,
        "measurementResolutionMs": 16.6667,
        "errorThresholdMs": 1,
        "runs": []
      }
      """;

    [Test]
    public void Summary_WithoutAFormatVersion_IsFormatOne()
    {
      var summary = AnalysisSummary.Parse(MinimalSummary);
      Assert.That(summary.FormatVersion, Is.EqualTo(1));
      Assert.That(summary.Runs, Is.Empty);
      Assert.That(summary.Markers, Is.Empty);
      Assert.That(summary.Warnings, Is.Empty);
    }

    [Test]
    public void Summary_OfANewerFormat_IsRefused()
    {
      string json = MinimalSummary.Replace("{", "{ \"formatVersion\": 2,", System.StringComparison.Ordinal);
      Assert.That(() => AnalysisSummary.Parse(json), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("update"));
    }

    [Test]
    public void FileNames_FollowTheRunIds()
    {
      Assert.That(AnalysisFiles.FramesFileName(3, 0), Is.EqualTo("run-3-frames.csv"));
      Assert.That(AnalysisFiles.FramesFileName(3, 1), Is.EqualTo("run-3-2-frames.csv"));
    }

    [Test]
    public void FramesCsv_ReadsByColumnName_WhateverTheOrderAndExtraColumns()
    {
      const string csv =
        "frameIndex,newColumn,segment,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,driftMs,flags,cpuBusyMs\n"
        + "7,x,0,116.6667,3,50,33.3333,2,1,-0.5,SkippedBefore|Late,\n";
      var rows = FramesCsv.Read(new StringReader(csv));
      Assert.That(rows, Has.Count.EqualTo(1));
      var row = rows[0];
      Assert.That(
        (row.FrameIndex, row.Segment, row.AnimationTicks, row.OnScreenTicks, row.DriftTicks),
        Is.EqualTo((7UL, 0, 1_166_667L, 333_333L, -5_000L))
      );
      Assert.That(row.Flags, Is.EqualTo(new[] { "SkippedBefore", "Late" }));
      Assert.That(row.CpuBusyTicks, Is.Null, "an empty cell");
      Assert.That(row.LastSeenTicks, Is.Null, "a column the file lacks");
    }

    [Test]
    public void Milliseconds_AreWholeTicks()
    {
      foreach (long ticks in new[] { 0L, 1L, 166_667L, -3L, 123_456_789_012L })
        Assert.That(Milliseconds.ParseTicks(Milliseconds.Format(ticks)), Is.EqualTo(ticks));
      Assert.That(Milliseconds.Format(166_667), Is.EqualTo("16.6667"));
    }
  }
}
