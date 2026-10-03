//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysing a long capture at 240 Hz (10 minutes and an hour: 144,000 and 864,000 captures): the timeline of its decoded rows, that is
//* splitting them into runs, building the presented frames, the pacing and the statistics. Reading the capture data and writing the output
//* files are not part of it (OutputFileBenchmarks has the files). Every 97th frame is held for two captures, as SyntheticHour's.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using MB.FramePacing.Analysis;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Benchmarks
{
  [BenchmarkCategory(BenchmarkCategories.LongRunning)]
  [MemoryDiagnoser]
  [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 5)]
  public class AnalysisBenchmarks
  {
    private const int RefreshHz = 240;
    private const uint RunId = 1;
    private const MB.FramePacing.Marker.MarkerFlags NoFlags = MB.FramePacing.Marker.MarkerFlags.NoFlags;

    private List<CaptureRow> m_rows = null!;

    [Params(10, 60)]
    public int Minutes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      int captures = RefreshHz * 60 * Minutes;
      long refresh = TimeSpan.TicksPerSecond / RefreshHz;
      m_rows = new List<CaptureRow>(captures);
      void Add(MarkerPayload payload, StartMetadata? start = null) =>
        m_rows.Add(new CaptureRow(m_rows.Count, new TickCount64(m_rows.Count * refresh), CaptureStatus.Decoded, payload, start));

      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceStart, RunId, 0, NoFlags, TimeSpan.Zero), StartMetadata.FromTag(0, "benchmark"));
      ulong frameIndex = 1;
      while (m_rows.Count < captures - 3)
      {
        // The frame's animation time is the time it is first captured at; every 97th stays for two captures
        var payload = new MarkerPayload(MarkerKind.Frame, RunId, frameIndex, NoFlags, new TimeSpan(m_rows.Count * refresh));
        Add(payload);
        if (frameIndex % 97 == 0)
          Add(payload);
        ++frameIndex;
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceEnd, RunId, frameIndex, NoFlags, TimeSpan.Zero));
    }

    [Benchmark]
    public int Timeline() => TimelineAnalyzer.Analyze(m_rows).Runs[0].Frames.Count;
  }
}
