//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json and the CSVs: the format version, the file names, times as whole nanoseconds, columns an older file lacks or a newer one adds, and
//* content that is refused.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
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
        "capturePeriodNs": 16666700,
        "measurementResolutionNs": 16666700,
        "errorThresholdNs": 1000000,
        "runs": []
      }
      """;

    private const string Counts = """
      "counts": { "captures": 10, "decoded": 9, "undecodable": 1, "torn": 0, "notRecorded": 0, "sourceDroppedFrames": 0, "missedCaptures": 0,
        "presentedFrames": 8, "skippedFrameIndices": 0, "droppedFrames": 0, "outOfOrderCaptures": 0, "segments": 1 }
      """;

    private const string Values = """{ "count": 3, "min": -1.5, "mean": 0.25, "stdDev": 1.25, "p50": 0, "p95": 2, "p99": 2.5, "max": 3 }""";

    private const string Statistics =
      "\"statistics\": { \"displayDeltaMs\": "
      + Values
      + ", \"animationDeltaMs\": "
      + Values
      + ", \"animationErrorMs\": "
      + Values
      + ", \"absoluteAnimationErrorMs\": "
      + Values
      + ", \"driftMs\": "
      + Values
      + ", \"onScreenMs\": "
      + Values
      + ", \"framesWithAnimationError\": 2, \"errorPerFrameMs\": 0.5, \"percentError\": 25, \"excludedStaticFrames\": 0, \"uncertainSteps\": 0 }";

    private const string RunStart = """
      "runId": 7, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "run-7-frames.csv"
      """;

    private const string FrameColumns =
      "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags";

    /// <summary>A summary whose one run has the given members.</summary>
    private static string OneRun(params string[] members) =>
      "{ \"capturePeriodNs\": 16666700, \"errorThresholdNs\": 1000000, \"runs\": [ { " + string.Join(", ", members) + " } ] }";

    private static string MinimalWith(string member) => MinimalSummary.Replace("{", "{ " + member + ",", StringComparison.Ordinal);

    private static string MinimalWithout(string name)
    {
      var lines = new List<string>(MinimalSummary.Split('\n'));
      Assert.That(lines.RemoveAll(line => line.Contains("\"" + name + "\"", StringComparison.Ordinal)), Is.EqualTo(1));
      return string.Join('\n', lines);
    }

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
      string json = MinimalWith("\"formatVersion\": 2");
      Assert.That(() => AnalysisSummary.Parse(json), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("update"));
    }

    [Test]
    public void Summary_TimesAreWholeNanoseconds()
    {
      var summary = AnalysisSummary.Parse(MinimalSummary);
      Assert.That(
        (summary.CapturePeriod, summary.MeasurementResolution, summary.ErrorThreshold),
        Is.EqualTo((new NanosecondTimeSpan(16_666_700), new NanosecondTimeSpan(16_666_700), new NanosecondTimeSpan(1_000_000)))
      );
      Assert.That(
        AnalysisSummary.Parse(MinimalWithout("measurementResolutionNs")).MeasurementResolution,
        Is.EqualTo(new NanosecondTimeSpan(16_666_700)),
        "a file without it: the capture period"
      );

      // Written as the nanoseconds they are, and read back the same
      string json = summary.ToJson();
      Assert.That(json, Does.Contain("\"capturePeriodNs\": 16666700"));
      Assert.That(json, Does.Contain("\"measurementResolutionNs\": 16666700"));
      Assert.That(json, Does.Contain("\"errorThresholdNs\": 1000000"));
      Assert.That(json, Does.Not.Contain("PeriodMs").And.Not.Contain("ThresholdMs"));
      Assert.That(AnalysisSummary.Parse(json).CapturePeriod, Is.EqualTo(summary.CapturePeriod));

      // Never a fraction, a text or a number of another unit's name
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("16666700,", "16666700.5,", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("16666700,", "16666700.0,", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>(),
        "a whole number written as a real one"
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            MinimalSummary.Replace("\"errorThresholdNs\": 1000000", "\"errorThresholdNs\": \"1000000\"", StringComparison.Ordinal)
          ),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("capturePeriodNs", "capturePeriodMs", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("capturePeriodNs")
      );
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("capturePeriodNs", "capturePeriodTicks", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("capturePeriodNs"),
        "a file from before the nanoseconds"
      );
    }

    [Test]
    public void Summary_PacingTimesAreWholeNanoseconds()
    {
      const string pacing = """
        "pacing": { "refreshPeriodNs": 16666700, "refreshCalculated": false, "targetFrameNs": 33333400, "source": "TargetFrameTime",
          "lateFrames": 1, "lateShare": 0.125, "worstLateShare": 0.5, "errorFramesWithUnevenDisplay": 1, "errorFramesWithEvenDisplay": 1,
          "verdict": "Both", "refreshHz": 59.99988 }
        """;
      var run = AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, pacing)).Runs[0];
      Assert.That(run.Pacing, Is.Not.Null);
      Assert.That(
        (run.Pacing!.RefreshPeriod, run.Pacing.TargetFrameTime),
        Is.EqualTo((new NanosecondTimeSpan(16_666_700), new NanosecondTimeSpan(33_333_400)))
      );
      Assert.That(
        (run.RunId, run.FramesFile, run.Counts.Captures, run.Statistics.FramesWithAnimationError),
        Is.EqualTo((7u, "run-7-frames.csv", 10L, 2L))
      );

      Assert.That(
        () => AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, pacing.Replace("33333400", "33333400.5", StringComparison.Ordinal))),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, pacing.Replace("refreshPeriodNs", "refreshPeriodMs", StringComparison.Ordinal))),
        Throws.InstanceOf<InvalidDataException>()
      );
    }

    [Test]
    public void Summary_RequiredFieldsAreRequired()
    {
      Assert.That(() => AnalysisSummary.Parse(MinimalWithout("capturePeriodNs")), Throws.InstanceOf<InvalidDataException>(), "no capture period");
      Assert.That(() => AnalysisSummary.Parse(MinimalWithout("errorThresholdNs")), Throws.InstanceOf<InvalidDataException>(), "no error threshold");
      Assert.That(() => AnalysisSummary.Parse(OneRun(RunStart, Statistics)), Throws.InstanceOf<InvalidDataException>(), "no counts");
      Assert.That(() => AnalysisSummary.Parse(OneRun(RunStart, Counts)), Throws.InstanceOf<InvalidDataException>(), "no statistics");
      Assert.That(() => AnalysisSummary.Parse(OneRun(Counts, Statistics)), Throws.InstanceOf<InvalidDataException>(), "no run id");
      Assert.That(
        () => AnalysisSummary.Parse(OneRun("\"runId\": 7, \"hasStartMarker\": true, \"hasEndMarker\": false", Counts, Statistics)),
        Throws.InstanceOf<InvalidDataException>(),
        "no frames file"
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(OneRun("\"runId\": 7, \"hasStartMarker\": true, \"hasEndMarker\": false, \"framesFile\": null", Counts, Statistics)),
        Throws.InstanceOf<InvalidDataException>(),
        "null is absent"
      );
      Assert.That(() => AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, "\"histograms\": {}")), Throws.InstanceOf<InvalidDataException>());
      Assert.That(() => AnalysisSummary.Parse(OneRun(RunStart, "\"counts\": 3", Statistics)), Throws.InstanceOf<InvalidDataException>());
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            OneRun(RunStart, Counts, Statistics.Replace("\"driftMs\": " + Values + ", ", string.Empty, StringComparison.Ordinal))
          ),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("driftMs"),
        "the statistics every run has"
      );
      Assert.That(
        () => AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics.Replace("\"p50\": 0, ", string.Empty, StringComparison.Ordinal))),
        Throws.InstanceOf<InvalidDataException>()
      );
      var statistics = AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics)).Runs[0].Statistics;
      Assert.That(statistics.DriftMs, Is.EqualTo(new ValueStatistics(3, -1.5, 0.25, 1.25, 0, 2, 2.5, 0, 3)), "p999 is 0 in a file without it");
      Assert.That(
        (statistics.CpuBusyMs, statistics.FrameTimeMs, statistics.CpuWaitMs),
        Is.EqualTo(((ValueStatistics?)null, (ValueStatistics?)null, (ValueStatistics?)null))
      );
    }

    [Test]
    public void Summary_ThatIsNotOne_IsRefusedAsInvalidData()
    {
      Assert.That(() => AnalysisSummary.Parse("[]"), Throws.InstanceOf<InvalidDataException>(), "not an object");
      Assert.That(() => AnalysisSummary.Parse("{ \"capturePeriodNs\": "), Throws.InstanceOf<InvalidDataException>(), "not JSON");
      Assert.That(() => AnalysisSummary.Parse(string.Empty), Throws.InstanceOf<InvalidDataException>(), "empty");
      Assert.That(() => AnalysisSummary.Parse("null"), Throws.InstanceOf<InvalidDataException>(), "null");
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            OneRun("\"runId\": -1, \"hasStartMarker\": true, \"hasEndMarker\": false, \"framesFile\": \"f\"", Counts, Statistics)
          ),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id below 0"
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            OneRun("\"runId\": 4294967296, \"hasStartMarker\": true, \"hasEndMarker\": false, \"framesFile\": \"f\"", Counts, Statistics)
          ),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id beyond 32 bits"
      );
      Assert.That(() => AnalysisSummary.Parse(MinimalWith("\"warnings\": \"one text\"")), Throws.InstanceOf<InvalidDataException>());
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("capturePeriodNs", "CapturePeriodNs", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>(),
        "names are case sensitive, as in the other languages' readers"
      );
      Assert.That(() => AnalysisSummary.Parse(MinimalWith("\"formatVersion\": -1")), Throws.InstanceOf<InvalidDataException>());
      Assert.That(AnalysisSummary.Parse(MinimalWith("\"formatVersion\": 0")).FormatVersion, Is.EqualTo(1), "as a file without it");
    }

    [Test]
    public void FileNames_FollowTheRunIds()
    {
      Assert.That(AnalysisFiles.FramesFileName(3, 0), Is.EqualTo("run-3-frames.csv"));
      Assert.That(AnalysisFiles.FramesFileName(3, 1), Is.EqualTo("run-3-2-frames.csv"));
    }

    [Test]
    public void FileNames_AndTheAnalysisFolder()
    {
      Assert.That(AnalysisFiles.RunFilePrefix(3, 0), Is.EqualTo("run-3"));
      Assert.That(AnalysisFiles.RunFilePrefix(uint.MaxValue, 2), Is.EqualTo("run-4294967295-3"));

      string folder = Path.Combine(Path.GetTempPath(), $"mb-framepacing-data-{Guid.NewGuid():N}");
      string analysis = Path.Combine(folder, AnalysisFiles.DirectoryName);
      Directory.CreateDirectory(analysis);
      try
      {
        Assert.That(AnalysisFiles.Find(folder), Is.Null, "no summary.json anywhere");
        Assert.That(AnalysisFiles.Find(Path.Combine(folder, "no-such-folder")), Is.Null);

        var summary = AnalysisSummary.Parse(MinimalSummary);
        summary.Write(Path.Combine(analysis, AnalysisFiles.SummaryFileName));
        Assert.That(AnalysisFiles.Find(folder), Is.EqualTo(analysis), "a capture folder");
        Assert.That(AnalysisFiles.Find(analysis), Is.EqualTo(analysis), "the analysis folder itself");
        summary.Write(Path.Combine(folder, AnalysisFiles.SummaryFileName));
        Assert.That(AnalysisFiles.Find(folder), Is.EqualTo(folder), "the folder's own summary wins");

        // Written as UTF-8 without a byte order mark, and read back as it was
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, AnalysisFiles.SummaryFileName));
        Assert.That(bytes[0], Is.EqualTo((byte)'{'));
        var read = AnalysisSummary.Read(Path.Combine(folder, AnalysisFiles.SummaryFileName));
        Assert.That(
          (read.CapturePeriod, read.ErrorThreshold, read.Scanout, read.AnalysedUtc),
          Is.EqualTo((summary.CapturePeriod, summary.ErrorThreshold, summary.Scanout, summary.AnalysedUtc))
        );
        Assert.That(read.ToJson(), Is.EqualTo(summary.ToJson()));
        Assert.That(() => AnalysisSummary.Read(Path.Combine(folder, "no-such-file.json")), Throws.InstanceOf<FileNotFoundException>());
      }
      finally
      {
        Directory.Delete(folder, recursive: true);
      }
    }

    [Test]
    public void FramesCsv_ReadsByColumnName_WhateverTheOrderAndExtraColumns()
    {
      const string csv =
        "frameIndex,newColumn,segment,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags,cpuBusyNs\n"
        + "7,x,0,116666700,3,50000000,33333300,2,1,-500000,SkippedBefore|Late,\n";
      var rows = FramesCsv.Read(new StringReader(csv));
      Assert.That(rows, Has.Count.EqualTo(1));
      var row = rows[0];
      Assert.That(
        (row.FrameIndex, row.Segment, row.AnimationTime, row.FirstSeenTime, row.OnScreen, row.Drift),
        Is.EqualTo(
          (
            7UL,
            0,
            new NanosecondTimeSpan(116_666_700),
            new NanosecondTickCount(50_000_000),
            new NanosecondTimeSpan(33_333_300),
            new NanosecondTimeSpan(-500_000)
          )
        )
      );
      Assert.That(row.Flags, Is.EqualTo(new[] { "SkippedBefore", "Late" }));
      Assert.That(row.CpuBusy, Is.Null, "an empty cell");
      Assert.That(row.LastSeenTime, Is.Null, "a column the file lacks");
    }

    [Test]
    public void FramesCsv_WritesEveryTimeAsItsNanoseconds_AndReadsThemBack()
    {
      // The values a marker can carry at their limits: nothing between the marker and the file converts them
      var row = new FrameRow(
        Segment: 2,
        FrameIndex: ulong.MaxValue,
        AnimationTime: NanosecondTimeSpan.MinValue,
        FirstCaptureIndex: 5,
        FirstSeenTime: new NanosecondTickCount(long.MaxValue),
        OnScreen: new NanosecondTimeSpan(16_666_700),
        Captures: 1,
        SkippedBefore: 0,
        DisplayDelta: new NanosecondTimeSpan(16_666_600),
        AnimationDelta: NanosecondTimeSpan.MaxValue,
        AnimationError: new NanosecondTimeSpan(-1),
        Drift: new NanosecondTimeSpan(-7),
        Flags: new[] { "Late" },
        IntendedDisplayTime: new NanosecondTickCount(long.MinValue),
        MarkerTargetFrameTime: NanosecondTimeDuration.FromNanoseconds(uint.MaxValue),
        TargetFrameTime: new NanosecondTimeSpan(33_333_400),
        MarkerPreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(1),
        PreferredFrameTime: new NanosecondTimeSpan(16_666_700),
        PacingError: new NanosecondTimeSpan(3),
        PredictionError: new NanosecondTimeSpan(-3),
        Lateness: new NanosecondTimeSpan(8_333_300),
        LastSeenTime: new NanosecondTickCount(1_234_567_890_123_456_789),
        CpuStartTime: new NanosecondTickCount(-1_234_567_890_123_456_789),
        CpuBusy: NanosecondTimeDuration.FromNanoseconds(12_006_000),
        FrameTime: new NanosecondTimeSpan(16_666_800),
        CpuWait: new NanosecondTimeSpan(4_660_800),
        OlderFrames: new[]
        {
          new OlderFrame(41, new NanosecondTickCount(1_234_567_890_123_456_790)),
          new OlderFrame(40, new NanosecondTickCount(-5)),
        },
        MainMarkerFirstSeenTime: new NanosecondTickCount(9_007_199_254_740_993),
        ScanoutDelay: new NanosecondTimeSpan(-9_007_199_254_740_993)
      );
      var written = new StringWriter { NewLine = "\n" };
      FramesCsv.Write(written, new[] { row }, camera: true);

      string[] lines = written.ToString().Split('\n');
      Assert.That(lines[0], Is.EqualTo(FramesCsv.Header + FramesCsv.CameraColumns));
      Assert.That(
        lines[1],
        Is.EqualTo(
          "2,18446744073709551615,-9223372036854775808,5,9223372036854775807,16666700,1,0,16666600,9223372036854775807,-1,-7,Late,"
            + "-9223372036854775808,4294967295,33333400,1,16666700,3,-3,8333300,1234567890123456789,-1234567890123456789,12006000,16666800,4660800,"
            + "41@1234567890123456790|40@-5,9007199254740993,-9007199254740993"
        )
      );

      var back = FramesCsv.Read(new StringReader(written.ToString()))[0];
      Assert.That(back with { Flags = row.Flags, OlderFrames = row.OlderFrames }, Is.EqualTo(row));
      Assert.That(back.Flags, Is.EqualTo(row.Flags));
      Assert.That(back.OlderFrames, Is.EqualTo(row.OlderFrames));

      // A marker's duration is a u32 in the file: one no marker carries is not written (no reader would take it)
      var beyond = NanosecondTimeDuration.FromNanoseconds(4_294_967_296);
      foreach (
        var tooLong in new[]
        {
          row with
          {
            MarkerTargetFrameTime = beyond,
          },
          row with
          {
            MarkerPreferredFrameTime = beyond,
          },
          row with
          {
            CpuBusy = beyond,
          },
        }
      )
        Assert.That(() => FramesCsv.Write(new StringWriter(), new[] { tooLong }, camera: false), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void FramesCsv_HeaderNamesEveryTimeInNanoseconds()
    {
      Assert.That(
        FramesCsv.Header,
        Is.EqualTo(
          "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,displayDeltaNs,"
            + "animationDeltaNs,animationErrorNs,driftNs,flags,intendedDisplayNs,markerTargetNs,targetNs,markerPreferredNs,"
            + "preferredNs,pacingErrorNs,predictionErrorNs,latenessNs,lastSeenNs,cpuStartNs,cpuBusyNs,frameTimeNs,"
            + "cpuWaitNs,olderFrames"
        )
      );
      Assert.That(FramesCsv.CameraColumns, Is.EqualTo(",mainMarkerFirstSeenNs,scanoutDelayNs"));
      Assert.That(
        CapturesCsv.Header,
        Is.EqualTo(
          "captureIndex,captureNs,status,kind,runId,frameIndex,animationNs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,"
            + "hostNs,deviceNs,payloadHex"
        )
      );
    }

    [TestCase("16.6667", TestName = "{m}(a fraction)")]
    [TestCase("1e3", TestName = "{m}(an exponent)")]
    [TestCase(" 5", TestName = "{m}(a space)")]
    [TestCase("+5", TestName = "{m}(a plus sign)")]
    [TestCase("1_000", TestName = "{m}(a digit separator)")]
    [TestCase("0x10", TestName = "{m}(hexadecimal)")]
    [TestCase("NaN", TestName = "{m}(not a number)")]
    [TestCase("9223372036854775808", TestName = "{m}(one more than 64 bits hold)")]
    [TestCase("-9223372036854775809", TestName = "{m}(one less than 64 bits hold)")]
    [TestCase("", TestName = "{m}(empty where a value is required)")]
    public void FramesCsv_ATimeThatIsNotWholeNanoseconds_IsRefused(string cell)
    {
      string csv = FrameColumns + "\n0,1," + cell + ",0,0,16666700,1,0,0,\n";
      Assert.That(() => FramesCsv.Read(new StringReader(csv)), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void FramesCsv_TheMarkersValuesMustFit32Bits()
    {
      IReadOnlyList<FrameRow> Read(string cpuBusy) =>
        FramesCsv.Read(new StringReader(FrameColumns + ",cpuBusyNs\n0,1,0,0,0,16666700,1,0,0,," + cpuBusy + "\n"));
      Assert.That(
        Read("4294967295")[0].CpuBusy,
        Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(uint.MaxValue)),
        "the largest: on demand in a frame time"
      );
      Assert.That(Read("8000000")[0].CpuBusy, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(8_000_000)));
      Assert.That(Read("0")[0].CpuBusy, Is.EqualTo(NanosecondTimeDuration.Zero));
      Assert.That(() => Read("4294967296"), Throws.InstanceOf<InvalidDataException>());
      Assert.That(() => Read("-1"), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void FramesCsv_OtherContentThatIsNotAFrame_IsRefusedAsInvalidData()
    {
      IReadOnlyList<FrameRow> Read(string header, string line) => FramesCsv.Read(new StringReader(header + "\n" + line + "\n"));
      Assert.That(() => FramesCsv.Read(new StringReader(string.Empty)), Throws.InstanceOf<InvalidDataException>(), "an empty file");
      Assert.That(FramesCsv.Read(new StringReader(FrameColumns + "\n\n")), Is.Empty, "a header and an empty line");
      Assert.That(() => Read(FrameColumns, "x,1,0,0,0,16666700,1,0,0,"), Throws.InstanceOf<InvalidDataException>(), "a segment that is no number");
      Assert.That(() => Read(FrameColumns, "0,-1,0,0,0,16666700,1,0,0,"), Throws.InstanceOf<InvalidDataException>(), "a negative frame index");
      Assert.That(
        () => Read(FrameColumns, "0,1,0,0,0,16666700,2147483648,0,0,"),
        Throws.InstanceOf<InvalidDataException>(),
        "captures beyond 32 bits"
      );
      Assert.That(() => Read("frameIndex,animationNs", "1,0"), Throws.InstanceOf<InvalidDataException>(), "a required column the file lacks");
      Assert.That(
        () => Read(FrameColumns.Replace("Ns", "Ticks", StringComparison.Ordinal), "0,1,0,0,0,166667,1,0,0,"),
        Throws.InstanceOf<InvalidDataException>(),
        "a file from before the nanoseconds"
      );
      Assert.That(() => Read(FrameColumns, "0,1,0,0,0,16666700,1,0,0,Late|"), Throws.InstanceOf<InvalidDataException>(), "an empty flag");
      Assert.That(
        () => FramesCsv.Read(new StringReader(FrameColumns + "\n0,1,0,0,0,16666700,1,0,0,\n\n0,2,1.5,0,0,16666700,1,0,0,\n"), "run-1-frames.csv"),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("'run-1-frames.csv' line 4").And.Message.Contains("'1.5'"),
        "the error names the file and the line"
      );
      foreach (string older in new[] { "41", "@5", "41@", "x@5", "41@5.5", "41@5|", "41@5||42@6", "|41@5" })
      {
        Assert.That(
          () => Read(FrameColumns + ",olderFrames", "0,1,0,0,0,16666700,1,0,0,," + older),
          Throws.InstanceOf<InvalidDataException>(),
          "olderFrames '" + older + "'"
        );
      }
      var frames = Read(FrameColumns + ",olderFrames", "0,1,0,0,0,16666700,1,0,0,,41@5|40@-6");
      Assert.That(
        frames[0].OlderFrames,
        Is.EqualTo(new[] { new OlderFrame(41, new NanosecondTickCount(5)), new OlderFrame(40, new NanosecondTickCount(-6)) })
      );
    }

    [Test]
    public void CapturesCsv_CarriesSourceDropsMissedRefreshesAndTheSyncMarker_AndWritesBackAsItWas()
    {
      const string csv =
        CapturesCsv.Header + "\r\n4,66666700,Torn,Frame,7,12,200000000,3,1,7,11,70100000,66666700,4D46\r\n" + "5,,NotRecorded,,,,,0,0,,,,,\r\n";
      var rows = CapturesCsv.Read(new StringReader(csv));

      Assert.That(rows, Has.Count.EqualTo(2));
      var torn = rows[0];
      Assert.That((torn.CaptureStatus, torn.FrameIndex, torn.SyncRunId, torn.SyncFrameIndex), Is.EqualTo(("Torn", (ulong?)12, (uint?)7, (ulong?)11)));
      Assert.That(
        (torn.CaptureTime, torn.AnimationTime, torn.HostTime, torn.DeviceTime),
        Is.EqualTo(
          (
            (NanosecondTickCount?)new NanosecondTickCount(66_666_700),
            (NanosecondTimeSpan?)new NanosecondTimeSpan(200_000_000),
            (NanosecondTickCount?)new NanosecondTickCount(70_100_000),
            (NanosecondTickCount?)new NanosecondTickCount(66_666_700)
          )
        )
      );
      Assert.That((torn.SourceDropsBefore, torn.MissedBefore), Is.EqualTo((3L, 1L)));
      Assert.That(torn.Payload, Is.EqualTo(new byte[] { 0x4D, 0x46 }));
      Assert.That((rows[1].CaptureTime, rows[1].SyncFrameIndex), Is.EqualTo(((NanosecondTickCount?)null, (ulong?)null)));

      var written = new StringWriter { NewLine = "\r\n" };
      CapturesCsv.Write(written, rows);
      Assert.That(written.ToString(), Is.EqualTo(csv));
    }

    [Test]
    public void CapturesCsv_ContentThatIsNotACapture_IsRefusedAsInvalidData()
    {
      IReadOnlyList<CaptureCsvRow> Read(string line) => CapturesCsv.Read(new StringReader(CapturesCsv.Header + "\n" + line + "\n"));
      Assert.That(
        Read("4,66666700,Decoded,Frame,4294967295,12,200000000,0,0,4294967295,11,70100000,66666700,4D46")[0].RunId,
        Is.EqualTo(uint.MaxValue)
      );
      Assert.That(
        () => Read("4,66666700,Decoded,Frame,-1,12,200000000,0,0,,,70100000,66666700,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id below 0"
      );
      Assert.That(
        () => Read("4,66666700,Decoded,Frame,4294967296,12,200000000,0,0,,,70100000,66666700,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id beyond 32 bits"
      );
      Assert.That(
        () => Read("4,66666700,Torn,Frame,7,12,200000000,0,0,4294967296,11,70100000,66666700,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a sync run id beyond 32 bits"
      );
      Assert.That(
        () => Read("4,66.6667,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666700,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a fraction"
      );
      Assert.That(
        () => Read("4,66666700,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666700,4D4"),
        Throws.InstanceOf<InvalidDataException>(),
        "half a byte"
      );
      Assert.That(
        () => Read("4,66666700,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666700,4D4G"),
        Throws.InstanceOf<InvalidDataException>(),
        "no hex digit"
      );
      Assert.That(
        () => Read("x,66666700,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666700,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "no capture index"
      );
      Assert.That(() => CapturesCsv.Read(new StringReader(string.Empty)), Throws.InstanceOf<InvalidDataException>(), "an empty file");
    }
  }
}
