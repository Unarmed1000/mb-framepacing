//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* End to end through the real recorder and files: the capture data (captures.mbcd) must hold the ground truth marker of every captured frame,
//* decoded live, and the frames themselves (frames.mbfc) are only stored when asked for.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class SyntheticCaptureTests
  {
    [Test]
    public void Scenario_GroundTruth()
    {
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          RefreshHz = 240,
          CaptureFps = 240,
          StartMarkerSeconds = 0.25,
          RunSeconds = 1,
          EndMarkerSeconds = 0.25,
          StallEvery = 10,
          SkipEvery = 7,
        }
      );
      var frames = scenario.PresentedFrames;
      Assert.That(frames[0].Payload.Kind, Is.EqualTo(MarkerKind.SequenceStart));
      Assert.That(frames[^1].Payload.Kind, Is.EqualTo(MarkerKind.SequenceEnd));
      for (int i = 1; i < frames.Count; ++i)
      {
        Assert.That(frames[i].DisplayTicks, Is.GreaterThan(frames[i - 1].DisplayTicks));
        Assert.That(frames[i].Payload.FrameIndex, Is.GreaterThan(frames[i - 1].Payload.FrameIndex));
      }
      // Skips make the frame index jump by two
      Assert.That(frames[7].Payload.FrameIndex - frames[6].Payload.FrameIndex, Is.EqualTo(2UL));
      Assert.That(scenario.CaptureCount, Is.EqualTo(360));
    }

    [Test]
    public void CaptureRunner_Unpaced_EveryRecordCarriesTheGroundTruthMarker()
    {
      using var temp = new TempDirectory();
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 500,
          RefreshHz = 500,
          RunSeconds = 1,
          StallEvery = 13,
          SkipEvery = 29,
        }
      );
      using var source = new SyntheticCaptureSource(scenario, paced: false);

      var result = CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = temp.Path, RingFrames = 4096 }, null, CancellationToken.None);

      Assert.That(result.Session.FramesDroppedByRecorder, Is.Zero);
      Assert.That(result.Session.FramesWritten, Is.EqualTo(scenario.CaptureCount));
      Assert.That(CaptureSessionInfo.TryLoad(temp.Path)!.FramesWritten, Is.EqualTo(scenario.CaptureCount));
      Assert.That(result.FramesPath, Is.Null, "no frames stored unless asked for");
      Assert.That(System.IO.File.Exists(System.IO.Path.Combine(temp.Path, CaptureSessionInfo.FramesFileName)), Is.False);
      Assert.That(result.Session.FramesStored, Is.False);

      using var reader = new CaptureDataReader(result.DataPath);
      var o = scenario.Options;
      Assert.That(reader.Header.FramesStored, Is.False);
      Assert.That(reader.Header.Locks, Has.Count.EqualTo(1), "the synthetic source draws the main marker only");
      Assert.That(reader.Header.Locks[0].Bounds.X, Is.EqualTo(o.OriginX));
      Assert.That(reader.Header.Locks[0].Bounds.Y, Is.EqualTo(o.OriginY));
      Assert.That(reader.Header.Locks[0].ModuleSizePx, Is.EqualTo(o.ModuleSizePx));
      Assert.That(reader.RecordCount, Is.EqualTo(scenario.CaptureCount));
      var records = reader.ReadAll();
      for (int i = 0; i < records.Length; ++i)
      {
        var record = records[i];
        Assert.That(record.CaptureIndex, Is.EqualTo(i));
        Assert.That(record.DeviceTicks, Is.EqualTo(scenario.CaptureTicks(i)), "the display timer");
        Assert.That(record.Status, Is.EqualTo(CaptureDataStatus.Decoded), $"record {i}");
        var expected = scenario.PresentedFrames[scenario.PresentedIndexAt(i)].Payload;
        var expectedStart = expected.Kind == MarkerKind.SequenceStart ? scenario.StartMetadata : null;
        Assert.That(record.MainBytes, Is.EqualTo(expected.Encode(expectedStart)), $"record {i}: the encoded QR bytes as read");
        Assert.That(MarkerPayload.TryDecode(record.MainBytes, out var payload, out var start), Is.True);
        Assert.That(payload, Is.EqualTo(expected), $"record {i}");
        Assert.That(start, Is.EqualTo(expectedStart));
      }
    }

    /// <summary>With KeepFrames the frames are stored too, one per capture data record.</summary>
    [Test]
    public void CaptureRunner_KeepFrames_StoresTheFramesOfEveryRecord()
    {
      using var temp = new TempDirectory();
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 240,
          RefreshHz = 240,
          RunSeconds = 0.5,
        }
      );
      using var source = new SyntheticCaptureSource(scenario, paced: false);

      var result = CaptureRunner.Run(
        source,
        new CaptureRunOptions
        {
          OutputDirectory = temp.Path,
          RingFrames = 4096,
          KeepFrames = true,
        },
        null,
        CancellationToken.None
      );

      Assert.That(result.Session.FramesStored, Is.True);
      using var data = new CaptureDataReader(result.DataPath);
      using var frames = new CaptureFileReader(result.FramesPath!);
      Assert.That(data.Header.FramesStored, Is.True);
      Assert.That(frames.RecordCount, Is.EqualTo(data.RecordCount));
      for (long i = 0; i < data.RecordCount; ++i)
      {
        var header = frames.ReadRecordHeader(i);
        var record = data.ReadRecord(i);
        Assert.That(record.CaptureIndex, Is.EqualTo(header.CaptureIndex));
        Assert.That(record.HostTicks, Is.EqualTo(header.HostTicks));
        Assert.That(record.DeviceTicks, Is.EqualTo(header.DeviceTicks));
      }
    }

    [Test]
    public void CaptureRunner_Paced_WaitForStartAndStopAtEnd()
    {
      using var temp = new TempDirectory();
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 240,
          RefreshHz = 240,
          StartMarkerSeconds = 0.4,
          RunSeconds = 0.6,
          EndMarkerSeconds = 0.4,
        }
      );
      using var source = new SyntheticCaptureSource(scenario, paced: true);

      var result = CaptureRunner.Run(
        source,
        new CaptureRunOptions
        {
          OutputDirectory = temp.Path,
          WaitForStart = true,
          StopAtEnd = true,
          EndTail = TimeSpan.FromMilliseconds(100),
        },
        null,
        CancellationToken.None
      );

      Assert.That(result.Session.StopReason, Is.EqualTo("end marker"));
      Assert.That(result.Session.SequenceRunId, Is.EqualTo(scenario.Options.RunId));
      Assert.That(result.Session.SequenceName, Is.EqualTo(scenario.Options.RunName));
      Assert.That(result.Session.FramesDroppedByRecorder, Is.Zero);

      // The recording must contain the whole measured run: some start frames, every run frame and some end frames
      using var reader = new CaptureDataReader(result.DataPath);
      long first = reader.ReadRecord(0).CaptureIndex;
      long last = reader.ReadRecord(reader.RecordCount - 1).CaptureIndex;
      Assert.That(scenario.PresentedFrames[scenario.PresentedIndexAt(first)].Payload.Kind, Is.EqualTo(MarkerKind.SequenceStart));
      Assert.That(scenario.PresentedFrames[scenario.PresentedIndexAt(last)].Payload.Kind, Is.EqualTo(MarkerKind.SequenceEnd));
      Assert.That(last, Is.LessThan(scenario.CaptureCount - 1), "stopped at the end marker, before the source ran out");
    }

    /// <summary>
    /// Like importing a video file: read far faster than real time, idle markers first, and the start and end markers in one capture each.
    /// Every frame is inspected, so both are found and the recording holds exactly the run.
    /// </summary>
    [Test]
    public void CaptureRunner_Unpaced_OneFrameStartAndEndMarkers_AreFound()
    {
      using var temp = new TempDirectory();
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 60,
          RefreshHz = 60,
          LeadInSeconds = 2,
          StartMarkerSeconds = 1.0 / 60,
          RunSeconds = 1,
          EndMarkerSeconds = 1.0 / 60,
          TailSeconds = 2,
        }
      );
      using var source = new SyntheticCaptureSource(scenario, paced: false);

      var result = CaptureRunner.Run(
        source,
        new CaptureRunOptions
        {
          OutputDirectory = temp.Path,
          WaitForStart = true,
          StopAtEnd = true,
          EndTail = TimeSpan.FromMilliseconds(100),
        },
        null,
        CancellationToken.None
      );

      var kinds = Enumerable
        .Range(0, (int)scenario.CaptureCount)
        .Select(c => scenario.PresentedFrames[scenario.PresentedIndexAt(c)].Payload.Kind)
        .ToList();
      long startCapture = kinds.IndexOf(MarkerKind.SequenceStart);
      long endCapture = kinds.IndexOf(MarkerKind.SequenceEnd);
      Assert.That(kinds.Count(k => k == MarkerKind.SequenceStart), Is.EqualTo(1), "the scenario shows the start marker in one capture");
      Assert.That(kinds.Count(k => k == MarkerKind.SequenceEnd), Is.EqualTo(1), "the scenario shows the end marker in one capture");

      Assert.That(result.Session.StopReason, Is.EqualTo("end marker"));
      Assert.That(result.Session.SequenceRunId, Is.EqualTo(scenario.Options.RunId));
      Assert.That(result.Session.FramesDroppedByRecorder, Is.Zero);

      using var reader = new CaptureDataReader(result.DataPath);
      long first = reader.ReadRecord(0).CaptureIndex;
      long last = reader.ReadRecord(reader.RecordCount - 1).CaptureIndex;
      Assert.That(first, Is.EqualTo(startCapture - 16), "16 frames of pre-roll before the start marker");
      Assert.That(last, Is.EqualTo(endCapture + 6), "100 ms of end tail at 60 fps, nothing after it");
      Assert.That(reader.RecordCount, Is.EqualTo(last - first + 1), "no frame of the run is missing");
    }
  }
}
