//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writing a playback report's page of a long run (10 minutes and an hour, at 60 and 240 Hz), without the video: the whole page (the report
//* cards, the zoom steps that fit, the data), its cards alone and its data alone, with the run's prepared data (RunChartData) already made,
//* as when the GUI saves the page of a run it shows. The video's copy is ffmpeg's or the disk's time, not measured here.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using MB.FramePacing.Charts;
using MB.FramePacing.Charts.Playback;

namespace MB.FramePacing.Benchmarks
{
  [MemoryDiagnoser]
  [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 5)]
  public class PlaybackBenchmarks
  {
    private static readonly string g_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-playback-benchmark");

    private RunSection m_section = null!;
    private IReadOnlyList<PlaybackCard> m_cards = null!;
    private PlaybackVideo m_video = null!;

    [Params(10, 60)]
    public int Minutes { get; set; }

    [Params(60, 240)]
    public int RefreshHz { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      var run = SyntheticHour.Create(RefreshHz * 60 * Minutes, refreshHz: RefreshHz);
      m_section = RunSection.Whole(run);
      m_video = new PlaybackVideo(
        PlaybackVideoKind.Linked,
        null,
        Path.Combine(g_directory, "recording.mp4"),
        1,
        DateTime.UnixEpoch,
        true,
        "h264 (High), yuv420p, mp4",
        null
      );
      // The data is prepared once per run, when it is first shown
      m_cards = PlaybackPage.Cards(m_section, ReportOptions.Default);
      Console.WriteLine($"// {Minutes} min at {RefreshHz} Hz: {m_cards.Count - 1} zoom step(s), the page {Page().Length / 1e6:0.0} MB");
    }

    /// <summary>The whole page: its cards (the whole report and the zoom steps), their SVG and the data.</summary>
    [Benchmark]
    public string Page() => PlaybackPage.Build(m_section, m_video, g_directory, g_directory);

    /// <summary>The cards alone: the whole report and every zoom step that fits.</summary>
    [Benchmark]
    public IReadOnlyList<PlaybackCard> Cards() => PlaybackPage.Cards(m_section, ReportOptions.Default);

    /// <summary>The data alone: the cards' plots and every frame's columns.</summary>
    [Benchmark]
    public string Data() => PlaybackData.Json(m_section, m_cards, ReportOptions.Default, m_video, g_directory, g_directory, "benchmark");
  }
}
