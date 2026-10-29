//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The playback page's data (PlaybackData): the frame columns, written small (steps and runs of equal values), read back as the page reads
//* them are every frame of the run or of a section exactly as the analysis has it, on the golden data (no ffmpeg needed).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MB.FramePacing.Analysis;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Charts.Playback;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PlaybackDataTests
  {
    private static readonly PlaybackVideo g_video = new PlaybackVideo(
      PlaybackVideoKind.Copied,
      "video.mp4",
      "recording.mp4",
      1,
      DateTime.UnixEpoch,
      true,
      "h264 (High), yuv420p, mp4",
      null
    );

    private static ChartRun GoldenRun()
    {
      string golden = Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "..", "..", "sdk", "test-data", "data", "60-busy-full-rate"));
      return AnalysisOutput.Read(golden).Single().Chart;
    }

    private static JsonDocument Data(RunSection section) =>
      JsonDocument.Parse(PlaybackData.Json(section, PlaybackPage.Cards(section, ReportOptions.Default), ReportOptions.Default, g_video, "test"));

    /// <summary>
    /// The page's cards leave out what the options hide (import --playback-hide pacing), as well as the title and the tiles, which the page's
    /// header has: every card, the whole report's and each zoom step's.
    /// </summary>
    [Test]
    public void Cards_LeaveOutWhatTheOptionsHide()
    {
      var section = RunSection.Whole(GoldenRun());
      static bool HasBand(PlaybackCard card) => card.Drawing.FlatShapes.OfType<TextRunsShape>().Any(t => t.Runs[0].Text == "PACER");
      var shown = PlaybackPage.Cards(section, ReportOptions.Default);
      Assert.That(shown, Has.Count.GreaterThan(1));
      Assert.That(shown.All(HasBand));
      Assert.That(shown.SelectMany(c => c.Drawing.FlatShapes.OfType<TextShape>()).Where(t => t.Class == "title"), Is.Empty, "no title");
      Assert.That(PlaybackPage.Cards(section, ReportOptions.Default.Hide(new[] { ReportItem.Pacing })).Any(HasBand), Is.False);
    }

    [Test]
    public void FrameColumns_ReadBack_AreTheRunsFrames()
    {
      var chart = GoldenRun();
      var section = RunSection.Whole(chart);
      using var data = Data(section);
      var root = data.RootElement;
      Assert.That(root.GetProperty("formatVersion").GetInt32(), Is.EqualTo(PlaybackData.FormatVersion));

      AssertFrames(root, section);
      // Frames that follow each other one by one are runs; the columns are a few bytes per frame
      Assert.That(PlaybackFrameColumns.FormOf(root, "index"), Is.EqualTo("rle"));
      Assert.That(root.GetProperty("frames").GetRawText().Length, Is.LessThan(12 * section.FrameCount));
    }

    [TestCase(2.0, 4.0)]
    [TestCase(0.0, 1.0)]
    [TestCase(3.0, 100.0)]
    public void FrameColumns_OfASection_AreItsFrames(double from, double to)
    {
      var chart = GoldenRun();
      var section = RunSection.Create(chart, from, Math.Min(to, RunSection.Whole(chart).ToSeconds));
      Assert.That(section.FrameCount, Is.GreaterThan(1));
      using var data = Data(section);

      AssertFrames(data.RootElement, section);
    }

    /// <summary>
    /// The video frames' QR, read as the page reads it, is what every capture row says: the frame index the QR carries (an older frame that
    /// came back included), none for a QR that was not decoded, a torn capture's sync marker, a capture index without a row. The golden
    /// capture decoded every capture, so the other cases are made up in its rows.
    /// </summary>
    [TestCase(0.0, 100.0)]
    [TestCase(2.0, 4.0)]
    public void CaptureColumns_ReadBack_AreWhatEveryVideoFramesQrSays(double from, double to)
    {
      var golden = GoldenRun();
      var rows = golden.Captures!.ToList();
      int k = rows.Count / 2;
      rows[k] = rows[k] with { FrameIndex = rows[k].FrameIndex - 3 };
      rows[k + 5] = rows[k + 5] with { CaptureStatus = nameof(CaptureStatus.Undecodable), Kind = null, RunId = null, FrameIndex = null };
      rows[k + 10] = rows[k + 10] with
      {
        CaptureStatus = nameof(CaptureStatus.Torn),
        SyncRunId = rows[k + 10].RunId,
        SyncFrameIndex = rows[k + 10].FrameIndex + 1,
      };
      rows[k + 12] = rows[k + 12] with { CaptureStatus = nameof(CaptureStatus.NotRecorded), Kind = null, RunId = null, FrameIndex = null };
      long removed = rows[k + 15].CaptureIndex;
      rows.RemoveAt(k + 15);
      var chart = golden with { Captures = rows };
      var section = RunSection.Create(chart, from, Math.Min(to, RunSection.Whole(chart).ToSeconds));
      using var data = Data(section);

      var (firstIndex, qr, sync, status) = PlaybackFrameColumns.ReadVideoFrames(data.RootElement)!.Value;
      var byIndex = rows.ToDictionary(r => r.CaptureIndex);
      // Every capture the section's video shows: from its first frame's first capture to its last frame's
      var frames = section.Data.Frames;
      Assert.That(firstIndex, Is.LessThanOrEqualTo(frames[section.Start].FirstCaptureIndex));
      Assert.That(firstIndex + qr.Length - 1, Is.GreaterThanOrEqualTo(frames[section.End - 1].FirstCaptureIndex));
      for (int i = 0; i < qr.Length; ++i)
      {
        long capture = firstIndex + i;
        if (!byIndex.TryGetValue(capture, out var row))
        {
          Assert.That((qr[i], sync[i], status[i]), Is.EqualTo(((long?)null, (long?)null, "noCapture")), $"capture {capture}");
          continue;
        }
        Assert.That(qr[i], Is.EqualTo((long?)row.FrameIndex), $"capture {capture}");
        Assert.That(sync[i], Is.EqualTo(row.CaptureStatus == nameof(CaptureStatus.Torn) ? (long?)row.SyncFrameIndex : null), $"capture {capture}");
        Assert.That(status[i], Is.EqualTo(JsonNamingPolicy.CamelCase.ConvertName(row.CaptureStatus)), $"capture {capture}");
      }
      if (from == 0)
      {
        Assert.That(status, Does.Contain("undecodable").And.Contain("torn").And.Contain("notRecorded").And.Contain("noCapture"));
        int older = (int)(rows[k].CaptureIndex - firstIndex);
        Assert.That(qr[older]!.Value, Is.LessThan(qr[older - 1]!.Value), "the older frame");
        Assert.That(status[(int)(removed - firstIndex)], Is.EqualTo("noCapture"));
      }
    }

    [Test]
    public void CaptureColumns_WithoutCaptureRows_AreLeftOut()
    {
      var chart = GoldenRun() with { Captures = null };
      using var data = Data(RunSection.Whole(chart));
      Assert.That(PlaybackFrameColumns.ReadVideoFrames(data.RootElement), Is.Null);
    }

    /// <summary>Every column of <paramref name="root"/>'s frames, read as the page reads them, is the section's frames as the analysis has them.</summary>
    private static void AssertFrames(JsonElement root, RunSection section)
    {
      var all = section.Data.Frames;
      var origin = section.Data.Origin;
      var indices = Enumerable.Range(section.Start, section.End - section.Start).ToList();
      var columns = PlaybackFrameColumns.Read(root);

      Assert.That(columns["t"], Is.EqualTo(indices.Select(i => (long?)(all[i].FirstSeenTime - origin).Nanoseconds)));
      Assert.That(columns["index"], Is.EqualTo(indices.Select(i => (long?)all[i].FrameIndex)));
      Assert.That(columns["capture"], Is.EqualTo(indices.Select(i => (long?)all[i].FirstCaptureIndex)));
      // A frame's step is the next frame's display time step: a section's last frame has it too when the run goes on
      Assert.That(columns["step"], Is.EqualTo(indices.Select(i => i + 1 < all.Count ? all[i + 1].DisplayDelta?.Nanoseconds : null)));
      Assert.That(columns["error"], Is.EqualTo(indices.Select(i => all[i].AnimationError?.Nanoseconds)));
      Assert.That(columns["hold"], Is.EqualTo(indices.Select(i => (long?)(long)section.Data.HoldKinds[i])));
      Assert.That(columns["flags"], Is.EqualTo(indices.Select(i => (long?)(long)all[i].Flags)));
    }
  }
}
