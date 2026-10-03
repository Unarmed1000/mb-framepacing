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

    /// <summary>Every column of <paramref name="root"/>'s frames, read as the page reads them, is the section's frames as the analysis has them.</summary>
    private static void AssertFrames(JsonElement root, RunSection section)
    {
      var all = section.Data.Frames;
      var origin = section.Data.Origin;
      var indices = Enumerable.Range(section.Start, section.End - section.Start).ToList();
      var columns = PlaybackFrameColumns.Read(root);

      Assert.That(columns["t"], Is.EqualTo(indices.Select(i => (long?)(all[i].FirstSeenTime - origin).Ticks)));
      Assert.That(columns["index"], Is.EqualTo(indices.Select(i => (long?)all[i].FrameIndex)));
      Assert.That(columns["capture"], Is.EqualTo(indices.Select(i => (long?)all[i].FirstCaptureIndex)));
      // A frame's step is the next frame's display time step: a section's last frame has it too when the run goes on
      Assert.That(columns["step"], Is.EqualTo(indices.Select(i => i + 1 < all.Count ? all[i + 1].DisplayDelta?.Ticks : null)));
      Assert.That(columns["error"], Is.EqualTo(indices.Select(i => all[i].AnimationError?.Ticks)));
      Assert.That(columns["hold"], Is.EqualTo(indices.Select(i => (long?)(long)section.Data.HoldKinds[i])));
      Assert.That(columns["flags"], Is.EqualTo(indices.Select(i => (long?)(long)all[i].Flags)));
    }
  }
}
