//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Refreshes a capture missed without saying so: found from the device clock's steps (never the host clock's), and a capture gap like the
//* ones the recorder and the source report: the steps around it are not judged, so the gap makes no late frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class MissedCapturesTests
  {
    private const long Period = TimeSpan.TicksPerSecond / 60;

    private static readonly CaptureDataHeader g_header = new CaptureDataHeader(
      960,
      540,
      60,
      1,
      960,
      540,
      new DataRect(0, 0, 960, 540),
      new[] { new MarkerLocation(new DataRect(32, 32, 147, 147), 3) },
      FramesStored: false,
      Camera: false
    );

    [TestCase(1.0, 0)]
    [TestCase(1.49, 0)]
    [TestCase(1.5, 1)]
    [TestCase(2.0, 1)]
    [TestCase(2.5, 2)]
    [TestCase(3.0, 2)]
    public void Before_CountsTheRefreshesAStepLeftOut(double periods, long missed) =>
      Assert.That(MissedCaptures.Before((long)Math.Round(periods * Period), Period), Is.EqualTo(missed));

    /// <summary>
    /// Records of a 60 Hz capture showing frames 1 to 6, one capture each; the capture of <paramref name="gapBefore"/> comes a refresh late
    /// (the one before it never arrived). <paramref name="deviceClock"/>: every record has a device timestamp, else none has one.
    /// </summary>
    private static List<CaptureDataRecord> Records(int gapBefore, bool deviceClock)
    {
      var records = new List<CaptureDataRecord>();
      long time = 0;
      void Add(MarkerPayload payload, StartMetadata? start = null)
      {
        records.Add(
          new CaptureDataRecord(
            records.Count,
            time,
            deviceClock ? time : CaptureDataRecord.UnknownTicks,
            0,
            CaptureDataStatus.Decoded,
            payload.Encode(start),
            null
          )
        );
        time += Period;
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceStart, 1, 0, MB.FrameMarker.MarkerFlags.None, 0), StartMetadata.Empty);
      for (ulong frame = 1; frame <= 6; ++frame)
      {
        if ((int)frame == gapBefore)
          time += Period;
        Add(
          new MarkerPayload(
            MarkerKind.Frame,
            1,
            frame,
            MB.FrameMarker.MarkerFlags.None,
            (long)frame * Period,
            PreferredFrameTicks: (uint)Period,
            TargetFrameTicks: (uint)Period
          )
        );
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceEnd, 1, 7, MB.FrameMarker.MarkerFlags.None, 7 * Period));
      return records;
    }

    [Test]
    public void DeviceClock_AStepOfTwoPeriods_MissedARefresh()
    {
      var rows = CaptureDecoder.FromData(g_header, Records(gapBefore: 4, deviceClock: true)).Rows;

      Assert.That(rows.Select(r => r.MissedBefore), Is.EqualTo(rows.Select(r => r.Payload.FrameIndex == 4 && r.IsDecoded ? 1L : 0L)));
      Assert.That(rows.Select(r => r.ToCsvRow().MissedBefore).Sum(), Is.EqualTo(1));
    }

    [Test]
    public void HostClock_IsNeverTakenForAMissedRefresh()
    {
      var rows = CaptureDecoder.FromData(g_header, Records(gapBefore: 4, deviceClock: false)).Rows;

      Assert.That(rows.Select(r => r.MissedBefore), Is.All.Zero, "host arrival times bunch and spread: a long step proves nothing");
    }

    /// <summary>
    /// Frame 4 appears two refreshes after frame 3 because the capture missed a refresh: frame 4 may have been on screen in it, so the steps
    /// around the gap are not judged, and frame 3 is not late. Without the missed refresh, frame 3's hold of two refreshes is late.
    /// </summary>
    [Test]
    public void MissedRefresh_IsACaptureGap_NotALateFrame()
    {
      var capture = CaptureDecoder.FromData(g_header, Records(gapBefore: 4, deviceClock: true));
      var run = TimelineAnalyzer.Analyze(capture.Rows).Runs.Single();

      Assert.That(run.Counts.MissedCaptures, Is.EqualTo(1));
      Assert.That(run.Frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.Late)), Is.Zero);
      Assert.That(run.Frames[3].Flags.HasFlag(PresentedFrameFlags.UncertainStart), "frame 4 was first seen after the gap");
      Assert.That(run.Frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStep)), Is.EqualTo(2));

      var hostRun = TimelineAnalyzer.Analyze(CaptureDecoder.FromData(g_header, Records(gapBefore: 4, deviceClock: false)).Rows).Runs.Single();
      Assert.That(hostRun.Counts.MissedCaptures, Is.Zero);
      Assert.That(hostRun.Frames[3].Flags.HasFlag(PresentedFrameFlags.Late), "on the host clock the long step is taken as it is");
    }
  }
}
