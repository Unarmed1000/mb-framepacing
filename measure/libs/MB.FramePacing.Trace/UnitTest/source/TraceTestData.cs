//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the trace tests share: the SDK's golden capture (sdk/test-data/data/60-busy-full-rate) as a run, the files of
//* measure/test-data/trace (made by tools/update_trace_test_data.py from tools/trace_augment.py: what this library has to equal), frames
//* made up for a test, and the user's trace processor.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MB.FramePacing.Capture;
using MB.FramePacing.Data;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  public static class TraceTestData
  {
    /// <summary>Set (to anything but 0) where a trace processor has to be there: the tests that need it then fail instead of being skipped.</summary>
    public const string RequiredVariable = "MB_REQUIRE_TRACE_PROCESSOR";

    public const long PeriodNs = 16_666_667;

    /// <summary>What the analysis lined the golden capture's two clocks up by: the capture's clock minus the pacer's.</summary>
    public const long CaptureMinusPacerNs = 50_000_000;

    public const string SyntheticTrace = "synthetic.pftrace";
    public const string PiecesTrace = "synthetic-pieces.pftrace";

    /// <summary>The repository's root: the folder that holds measure/test-data/trace.</summary>
    public static string Root()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "measure", "test-data", "trace")))
        directory = directory.Parent;
      return directory?.FullName
        ?? throw new DirectoryNotFoundException("Could not locate measure/test-data/trace above " + TestContext.CurrentContext.TestDirectory);
    }

    /// <summary>A file of measure/test-data/trace.</summary>
    public static string File(string name) => Path.Combine(Root(), "measure", "test-data", "trace", name);

    public static string GoldenCapture() => Path.Combine(Root(), "sdk", "test-data", "data", "60-busy-full-rate");

    /// <summary>The golden capture's one run.</summary>
    public static TraceRun GoldenRun()
    {
      string analysis = AnalysisFiles.Find(GoldenCapture()) ?? throw new DirectoryNotFoundException("The golden capture has no analysis");
      var summary = AnalysisSummary.Read(Path.Combine(analysis, AnalysisFiles.SummaryFileName));
      var run = summary.Runs[0];
      var frames = FramesCsv.Read(Path.Combine(analysis, run.FramesFile));
      var captures = CapturesCsv.Read(Path.Combine(analysis, AnalysisFiles.CapturesFileName));
      return TraceRun.Of(summary, run, frames, captures);
    }

    /// <summary>digest.json: the numbers the stored traces were placed by, and the length and SHA-256 of what the script wrote.</summary>
    public static JsonElement Digest()
    {
      using var document = JsonDocument.Parse(System.IO.File.ReadAllText(File("digest.json")));
      return document.RootElement.Clone();
    }

    public static (long Length, string Sha256) DigestOf(string path)
    {
      var bytes = System.IO.File.ReadAllBytes(path);
      return (bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static (long Length, string Sha256) Expected(string name)
    {
      var entry = Digest().GetProperty(name);
      return (entry.GetProperty("length").GetInt64(), entry.GetProperty("sha256").GetString()!);
    }

    /// <summary>The options the stored traces were joined with.</summary>
    public static TraceOptions Options(string trace, bool withProcess)
    {
      var digest = Digest();
      return new TraceOptions
      {
        Path = File(trace),
        Process = withProcess ? digest.GetProperty("process").GetString() : null,
        AppClockOffsetNs = digest.GetProperty("appClockOffsetNs").GetInt64(),
      };
    }

    /// <summary>The user's trace processor. Without one the test is skipped, or fails where <see cref="RequiredVariable"/> is set.</summary>
    public static string FindProcessorOrIgnore()
    {
      try
      {
        return TraceProcessorLocator.Find(null, FramePacingConfig.Load());
      }
      catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
      {
        string required = Environment.GetEnvironmentVariable(RequiredVariable) ?? string.Empty;
        if (required.Length > 0 && required != "0")
          Assert.Fail("Perfetto's trace processor is required here and was not found: " + ex.Message);
        Assert.Ignore("Perfetto's trace processor is not installed: " + ex.Message);
        throw;
      }
    }

    /// <summary>A frame made up for a test: what the join reads of one, with nothing a test does not set.</summary>
    public static FrameRow Frame(
      ulong frameIndex,
      long firstSeenNs,
      long onScreenNs = PeriodNs,
      int captures = 1,
      ulong skippedBefore = 0,
      long? displayDeltaNs = null,
      long? animationDeltaNs = null,
      long? animationErrorNs = null,
      string[]? flags = null,
      long? intendedDisplayNs = null,
      long? targetNs = null,
      long? latenessNs = null,
      long? cpuStartNs = null,
      long? cpuBusyNs = null,
      (ulong FrameIndex, long CaptureNs)[]? olderFrames = null
    ) =>
      new FrameRow(
        Segment: 0,
        FrameIndex: frameIndex,
        AnimationTime: default,
        FirstCaptureIndex: 0,
        FirstSeenTime: NanosecondTickCount.FromNanoseconds(firstSeenNs),
        OnScreen: NanosecondTimeSpan.FromNanoseconds(onScreenNs),
        Captures: captures,
        SkippedBefore: skippedBefore,
        DisplayDelta: Span(displayDeltaNs),
        AnimationDelta: Span(animationDeltaNs),
        AnimationError: Span(animationErrorNs),
        Drift: default,
        Flags: flags ?? Array.Empty<string>(),
        IntendedDisplayTime: Point(intendedDisplayNs),
        MarkerTargetFrameTime: null,
        TargetFrameTime: Span(targetNs),
        MarkerPreferredFrameTime: null,
        PreferredFrameTime: null,
        PacingError: null,
        PredictionError: null,
        Lateness: Span(latenessNs),
        LastSeenTime: null,
        CpuStartTime: Point(cpuStartNs),
        CpuBusy: cpuBusyNs is { } busy ? NanosecondTimeDuration.FromNanoseconds(busy) : null,
        FrameTime: null,
        CpuWait: null,
        OlderFrames: (olderFrames ?? Array.Empty<(ulong, long)>())
          .Select(older => new OlderFrame(older.FrameIndex, NanosecondTickCount.FromNanoseconds(older.CaptureNs)))
          .ToList()
      );

    /// <summary>A capture made up for a test.</summary>
    public static CaptureCsvRow MadeCapture(
      long captureIndex,
      long? captureNs,
      string status = "Decoded",
      long sourceDropsBefore = 0,
      long missedBefore = 0
    ) =>
      new CaptureCsvRow(
        captureIndex,
        Point(captureNs),
        status,
        null,
        null,
        null,
        null,
        sourceDropsBefore,
        missedBefore,
        null,
        null,
        null,
        null,
        null
      );

    public static IReadOnlyList<FrameRow> Frames(params FrameRow[] frames) => frames;

    private static NanosecondTimeSpan? Span(long? nanoseconds) => nanoseconds is { } value ? NanosecondTimeSpan.FromNanoseconds(value) : null;

    private static NanosecondTickCount? Point(long? nanoseconds) => nanoseconds is { } value ? NanosecondTickCount.FromNanoseconds(value) : null;
  }
}
