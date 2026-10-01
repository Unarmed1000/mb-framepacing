//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json and the CSVs: the format version, the file names, times as whole ticks, columns an older file lacks or a newer one adds, and
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
        "capturePeriodTicks": 166667,
        "measurementResolutionTicks": 166667,
        "errorThresholdTicks": 10000,
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
      "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,driftTicks,flags";

    /// <summary>A summary whose one run has the given members.</summary>
    private static string OneRun(params string[] members) =>
      "{ \"capturePeriodTicks\": 166667, \"errorThresholdTicks\": 10000, \"runs\": [ { " + string.Join(", ", members) + " } ] }";

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
    public void Summary_TimesAreWholeTicks()
    {
      var summary = AnalysisSummary.Parse(MinimalSummary);
      Assert.That(
        (summary.CapturePeriod, summary.MeasurementResolution, summary.ErrorThreshold),
        Is.EqualTo((new TimeSpan(166_667), new TimeSpan(166_667), new TimeSpan(10_000)))
      );
      Assert.That(
        AnalysisSummary.Parse(MinimalWithout("measurementResolutionTicks")).MeasurementResolution,
        Is.EqualTo(new TimeSpan(166_667)),
        "a file without it: the capture period"
      );

      // Written as the ticks they are, and read back the same
      string json = summary.ToJson();
      Assert.That(json, Does.Contain("\"capturePeriodTicks\": 166667"));
      Assert.That(json, Does.Contain("\"measurementResolutionTicks\": 166667"));
      Assert.That(json, Does.Contain("\"errorThresholdTicks\": 10000"));
      Assert.That(json, Does.Not.Contain("PeriodMs").And.Not.Contain("ThresholdMs"));
      Assert.That(AnalysisSummary.Parse(json).CapturePeriod, Is.EqualTo(summary.CapturePeriod));

      // Never a fraction, a text or a number of another unit's name
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("166667,", "166667.5,", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("166667,", "166667.0,", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>(),
        "a whole number written as a real one"
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            MinimalSummary.Replace("\"errorThresholdTicks\": 10000", "\"errorThresholdTicks\": \"10000\"", StringComparison.Ordinal)
          ),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () => AnalysisSummary.Parse(MinimalSummary.Replace("capturePeriodTicks", "capturePeriodMs", StringComparison.Ordinal)),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("capturePeriodTicks")
      );
    }

    [Test]
    public void Summary_PacingTimesAreWholeTicks()
    {
      const string pacing = """
        "pacing": { "refreshPeriodTicks": 166667, "refreshCalculated": false, "targetFrameTicks": 333334, "source": "TargetFrameTime",
          "lateFrames": 1, "lateShare": 0.125, "worstLateShare": 0.5, "errorFramesWithUnevenDisplay": 1, "errorFramesWithEvenDisplay": 1,
          "verdict": "Both", "refreshHz": 59.99988 }
        """;
      var run = AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, pacing)).Runs[0];
      Assert.That(run.Pacing, Is.Not.Null);
      Assert.That((run.Pacing!.RefreshPeriod, run.Pacing.TargetFrameTime), Is.EqualTo((new TimeSpan(166_667), new TimeSpan(333_334))));
      Assert.That(
        (run.RunId, run.FramesFile, run.Counts.Captures, run.Statistics.FramesWithAnimationError),
        Is.EqualTo((7u, "run-7-frames.csv", 10L, 2L))
      );

      Assert.That(
        () => AnalysisSummary.Parse(OneRun(RunStart, Counts, Statistics, pacing.Replace("333334", "333334.5", StringComparison.Ordinal))),
        Throws.InstanceOf<InvalidDataException>()
      );
      Assert.That(
        () =>
          AnalysisSummary.Parse(
            OneRun(RunStart, Counts, Statistics, pacing.Replace("refreshPeriodTicks", "refreshPeriodMs", StringComparison.Ordinal))
          ),
        Throws.InstanceOf<InvalidDataException>()
      );
    }

    [Test]
    public void Summary_RequiredFieldsAreRequired()
    {
      Assert.That(() => AnalysisSummary.Parse(MinimalWithout("capturePeriodTicks")), Throws.InstanceOf<InvalidDataException>(), "no capture period");
      Assert.That(
        () => AnalysisSummary.Parse(MinimalWithout("errorThresholdTicks")),
        Throws.InstanceOf<InvalidDataException>(),
        "no error threshold"
      );
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
      Assert.That(() => AnalysisSummary.Parse("{ \"capturePeriodTicks\": "), Throws.InstanceOf<InvalidDataException>(), "not JSON");
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
        () => AnalysisSummary.Parse(MinimalSummary.Replace("capturePeriodTicks", "CapturePeriodTicks", StringComparison.Ordinal)),
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
    public void FramesCsv_ReadsByColumnName_WhateverTheOrderAndExtraColumns()
    {
      const string csv =
        "frameIndex,newColumn,segment,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,driftTicks,flags,cpuBusyTicks\n"
        + "7,x,0,1166667,3,500000,333333,2,1,-5000,SkippedBefore|Late,\n";
      var rows = FramesCsv.Read(new StringReader(csv));
      Assert.That(rows, Has.Count.EqualTo(1));
      var row = rows[0];
      Assert.That(
        (row.FrameIndex, row.Segment, row.AnimationTime, row.FirstSeenTime, row.OnScreen, row.Drift),
        Is.EqualTo((7UL, 0, new TimeSpan(1_166_667), new TickCount64(500_000), new TimeSpan(333_333), new TimeSpan(-5_000)))
      );
      Assert.That(row.Flags, Is.EqualTo(new[] { "SkippedBefore", "Late" }));
      Assert.That(row.CpuBusy, Is.Null, "an empty cell");
      Assert.That(row.LastSeenTime, Is.Null, "a column the file lacks");
    }

    [Test]
    public void FramesCsv_WritesEveryTimeAsItsTicks_AndReadsThemBack()
    {
      // The values a marker can carry at their limits: nothing between the marker and the file converts them
      var row = new FrameRow(
        Segment: 2,
        FrameIndex: ulong.MaxValue,
        AnimationTime: TimeSpan.MinValue,
        FirstCaptureIndex: 5,
        FirstSeenTime: new TickCount64(long.MaxValue),
        OnScreen: new TimeSpan(166_667),
        Captures: 1,
        SkippedBefore: 0,
        DisplayDelta: new TimeSpan(166_666),
        AnimationDelta: TimeSpan.MaxValue,
        AnimationError: new TimeSpan(-1),
        Drift: new TimeSpan(-7),
        Flags: new[] { "Late" },
        IntendedDisplayTime: new TickCount64(long.MinValue),
        MarkerTargetFrameTime: TimeSpan32.MaxValue,
        TargetFrameTime: new TimeSpan(333_334),
        MarkerPreferredFrameTime: new TimeSpan32(1),
        PreferredFrameTime: new TimeSpan(166_667),
        PacingError: new TimeSpan(3),
        PredictionError: new TimeSpan(-3),
        Lateness: new TimeSpan(83_333),
        LastSeenTime: new TickCount64(1_234_567_890_123_456_789),
        CpuStartTime: new TickCount64(-1_234_567_890_123_456_789),
        CpuBusy: new TimeSpan32(120_060),
        FrameTime: new TimeSpan(166_668),
        CpuWait: new TimeSpan(46_608),
        OlderFrames: new[] { new OlderFrame(41, new TickCount64(1_234_567_890_123_456_790)), new OlderFrame(40, new TickCount64(-5)) },
        MainMarkerFirstSeenTime: new TickCount64(9_007_199_254_740_993),
        ScanoutDelay: new TimeSpan(-9_007_199_254_740_993)
      );
      var written = new StringWriter { NewLine = "\n" };
      FramesCsv.Write(written, new[] { row }, camera: true);

      string[] lines = written.ToString().Split('\n');
      Assert.That(lines[0], Is.EqualTo(FramesCsv.Header + FramesCsv.CameraColumns));
      Assert.That(
        lines[1],
        Is.EqualTo(
          "2,18446744073709551615,-9223372036854775808,5,9223372036854775807,166667,1,0,166666,9223372036854775807,-1,-7,Late,"
            + "-9223372036854775808,4294967295,333334,1,166667,3,-3,83333,1234567890123456789,-1234567890123456789,120060,166668,46608,"
            + "41@1234567890123456790|40@-5,9007199254740993,-9007199254740993"
        )
      );

      var back = FramesCsv.Read(new StringReader(written.ToString()))[0];
      Assert.That(back with { Flags = row.Flags, OlderFrames = row.OlderFrames }, Is.EqualTo(row));
      Assert.That(back.Flags, Is.EqualTo(row.Flags));
      Assert.That(back.OlderFrames, Is.EqualTo(row.OlderFrames));
    }

    [Test]
    public void FramesCsv_HeaderNamesEveryTimeInTicks()
    {
      Assert.That(
        FramesCsv.Header,
        Is.EqualTo(
          "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,displayDeltaTicks,"
            + "animationDeltaTicks,animationErrorTicks,driftTicks,flags,intendedDisplayTicks,markerTargetTicks,targetTicks,markerPreferredTicks,"
            + "preferredTicks,pacingErrorTicks,predictionErrorTicks,latenessTicks,lastSeenTicks,cpuStartTicks,cpuBusyTicks,frameTimeTicks,"
            + "cpuWaitTicks,olderFrames"
        )
      );
      Assert.That(FramesCsv.CameraColumns, Is.EqualTo(",mainMarkerFirstSeenTicks,scanoutDelayTicks"));
      Assert.That(
        CapturesCsv.Header,
        Is.EqualTo(
          "captureIndex,captureTicks,status,kind,runId,frameIndex,animationTicks,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,"
            + "hostTicks,deviceTicks,payloadHex"
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
    public void FramesCsv_ATimeThatIsNotWholeTicks_IsRefused(string cell)
    {
      string csv = FrameColumns + "\n0,1," + cell + ",0,0,166667,1,0,0,\n";
      Assert.That(() => FramesCsv.Read(new StringReader(csv)), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void FramesCsv_TheMarkersValuesMustFit32Bits()
    {
      IReadOnlyList<FrameRow> Read(string cpuBusy) =>
        FramesCsv.Read(new StringReader(FrameColumns + ",cpuBusyTicks\n0,1,0,0,0,166667,1,0,0,," + cpuBusy + "\n"));
      Assert.That(Read("4294967295")[0].CpuBusy, Is.EqualTo(TimeSpan32.MaxValue), "the largest: on demand in a frame time");
      Assert.That(Read("80000")[0].CpuBusy, Is.EqualTo(new TimeSpan32(80_000)));
      Assert.That(Read("0")[0].CpuBusy, Is.EqualTo(TimeSpan32.Zero));
      Assert.That(() => Read("4294967296"), Throws.InstanceOf<InvalidDataException>());
      Assert.That(() => Read("-1"), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void FramesCsv_OtherContentThatIsNotAFrame_IsRefusedAsInvalidData()
    {
      IReadOnlyList<FrameRow> Read(string header, string line) => FramesCsv.Read(new StringReader(header + "\n" + line + "\n"));
      Assert.That(() => FramesCsv.Read(new StringReader(string.Empty)), Throws.InstanceOf<InvalidDataException>(), "an empty file");
      Assert.That(FramesCsv.Read(new StringReader(FrameColumns + "\n\n")), Is.Empty, "a header and an empty line");
      Assert.That(() => Read(FrameColumns, "x,1,0,0,0,166667,1,0,0,"), Throws.InstanceOf<InvalidDataException>(), "a segment that is no number");
      Assert.That(() => Read(FrameColumns, "0,-1,0,0,0,166667,1,0,0,"), Throws.InstanceOf<InvalidDataException>(), "a negative frame index");
      Assert.That(() => Read(FrameColumns, "0,1,0,0,0,166667,2147483648,0,0,"), Throws.InstanceOf<InvalidDataException>(), "captures beyond 32 bits");
      Assert.That(() => Read("frameIndex,animationTicks", "1,0"), Throws.InstanceOf<InvalidDataException>(), "a required column the file lacks");
      Assert.That(() => Read(FrameColumns, "0,1,0,0,0,166667,1,0,0,Late|"), Throws.InstanceOf<InvalidDataException>(), "an empty flag");
      Assert.That(
        () => FramesCsv.Read(new StringReader(FrameColumns + "\n0,1,0,0,0,166667,1,0,0,\n\n0,2,1.5,0,0,166667,1,0,0,\n"), "run-1-frames.csv"),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("'run-1-frames.csv' line 4").And.Message.Contains("'1.5'"),
        "the error names the file and the line"
      );
      foreach (string older in new[] { "41", "@5", "41@", "x@5", "41@5.5", "41@5|", "41@5||42@6", "|41@5" })
      {
        Assert.That(
          () => Read(FrameColumns + ",olderFrames", "0,1,0,0,0,166667,1,0,0,," + older),
          Throws.InstanceOf<InvalidDataException>(),
          "olderFrames '" + older + "'"
        );
      }
      var frames = Read(FrameColumns + ",olderFrames", "0,1,0,0,0,166667,1,0,0,,41@5|40@-6");
      Assert.That(frames[0].OlderFrames, Is.EqualTo(new[] { new OlderFrame(41, new TickCount64(5)), new OlderFrame(40, new TickCount64(-6)) }));
    }

    [Test]
    public void CapturesCsv_CarriesSourceDropsMissedRefreshesAndTheSyncMarker_AndWritesBackAsItWas()
    {
      const string csv =
        CapturesCsv.Header + "\r\n4,666667,Torn,Frame,7,12,2000000,3,1,7,11,701000,666667,4D46\r\n" + "5,,NotRecorded,,,,,0,0,,,,,\r\n";
      var rows = CapturesCsv.Read(new StringReader(csv));

      Assert.That(rows, Has.Count.EqualTo(2));
      var torn = rows[0];
      Assert.That((torn.Status, torn.FrameIndex, torn.SyncRunId, torn.SyncFrameIndex), Is.EqualTo(("Torn", (ulong?)12, (uint?)7, (ulong?)11)));
      Assert.That(
        (torn.CaptureTime, torn.AnimationTime, torn.HostTime, torn.DeviceTime),
        Is.EqualTo(
          (
            (TickCount64?)new TickCount64(666_667),
            (TimeSpan?)new TimeSpan(2_000_000),
            (TickCount64?)new TickCount64(701_000),
            (TickCount64?)new TickCount64(666_667)
          )
        )
      );
      Assert.That((torn.SourceDropsBefore, torn.MissedBefore), Is.EqualTo((3L, 1L)));
      Assert.That(torn.Payload, Is.EqualTo(new byte[] { 0x4D, 0x46 }));
      Assert.That((rows[1].CaptureTime, rows[1].SyncFrameIndex), Is.EqualTo(((TickCount64?)null, (ulong?)null)));

      var written = new StringWriter { NewLine = "\r\n" };
      CapturesCsv.Write(written, rows);
      Assert.That(written.ToString(), Is.EqualTo(csv));
    }

    [Test]
    public void CapturesCsv_ContentThatIsNotACapture_IsRefusedAsInvalidData()
    {
      IReadOnlyList<CaptureCsvRow> Read(string line) => CapturesCsv.Read(new StringReader(CapturesCsv.Header + "\n" + line + "\n"));
      Assert.That(Read("4,666667,Decoded,Frame,4294967295,12,2000000,0,0,4294967295,11,701000,666667,4D46")[0].RunId, Is.EqualTo(uint.MaxValue));
      Assert.That(
        () => Read("4,666667,Decoded,Frame,-1,12,2000000,0,0,,,701000,666667,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id below 0"
      );
      Assert.That(
        () => Read("4,666667,Decoded,Frame,4294967296,12,2000000,0,0,,,701000,666667,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a run id beyond 32 bits"
      );
      Assert.That(
        () => Read("4,666667,Torn,Frame,7,12,2000000,0,0,4294967296,11,701000,666667,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a sync run id beyond 32 bits"
      );
      Assert.That(
        () => Read("4,66.6667,Decoded,Frame,7,12,2000000,0,0,,,701000,666667,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "a fraction"
      );
      Assert.That(
        () => Read("4,666667,Decoded,Frame,7,12,2000000,0,0,,,701000,666667,4D4"),
        Throws.InstanceOf<InvalidDataException>(),
        "half a byte"
      );
      Assert.That(
        () => Read("4,666667,Decoded,Frame,7,12,2000000,0,0,,,701000,666667,4D4G"),
        Throws.InstanceOf<InvalidDataException>(),
        "no hex digit"
      );
      Assert.That(
        () => Read("x,666667,Decoded,Frame,7,12,2000000,0,0,,,701000,666667,4D46"),
        Throws.InstanceOf<InvalidDataException>(),
        "no capture index"
      );
      Assert.That(() => CapturesCsv.Read(new StringReader(string.Empty)), Throws.InstanceOf<InvalidDataException>(), "an empty file");
    }
  }
}
