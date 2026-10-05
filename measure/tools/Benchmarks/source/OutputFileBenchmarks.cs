//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writing and reading the analysis output's CSV files of a long run at 240 Hz (10 minutes and an hour: 144,000 and 864,000 lines each): the
//* frames CSV and captures.csv, written into nothing and read from memory, so the numbers are the data library's formatting and parsing and
//* not the disk's. A run's analysis writes both files once, and every report made from the files (render, the GUI opening a capture)
//* reads both.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Benchmarks
{
  [BenchmarkCategory(BenchmarkCategories.LongRunning)]
  [MemoryDiagnoser]
  [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 5)]
  public class OutputFileBenchmarks
  {
    private const int RefreshHz = 240;

    private List<FrameRow> m_frames = null!;
    private List<CaptureCsvRow> m_captures = null!;
    private string m_framesCsv = null!;
    private string m_capturesCsv = null!;

    [Params(10, 60)]
    public int Minutes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      var frames = SyntheticHour.Create(RefreshHz * 60 * Minutes, refreshHz: RefreshHz).Run.Frames;
      m_frames = frames.Select(f => f.ToRow()).ToList();
      // A capture per frame, each with a frame marker's 57 bytes
      var payload = Enumerable.Range(0, 57).Select(i => (byte)(i * 5)).ToArray();
      m_captures = frames
        .Select(f => new CaptureCsvRow(
          f.FirstCaptureIndex,
          f.FirstSeenTime,
          "Decoded",
          "Frame",
          1,
          f.FrameIndex,
          f.AnimationTime,
          0,
          0,
          null,
          null,
          f.FirstSeenTime,
          f.FirstSeenTime,
          payload
        ))
        .ToList();
      var text = new StringWriter();
      FramesCsv.Write(text, m_frames, camera: false);
      m_framesCsv = text.ToString();
      text = new StringWriter();
      CapturesCsv.Write(text, m_captures);
      m_capturesCsv = text.ToString();
    }

    [Benchmark]
    public void WriteFrames() => FramesCsv.Write(TextWriter.Null, m_frames, camera: false);

    [Benchmark]
    public int ReadFrames() => FramesCsv.Read(new StringReader(m_framesCsv)).Count;

    [Benchmark]
    public void WriteCaptures() => CapturesCsv.Write(TextWriter.Null, m_captures);

    [Benchmark]
    public int ReadCaptures() => CapturesCsv.Read(new StringReader(m_capturesCsv)).Count;
  }
}
