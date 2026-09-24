//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* End to end through the real recorder and file: the synthetic source's frames must come back out of frames.mbfc with the ground truth
//* marker in every record.
//*
//* (c) 2026 Mana Battery
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
          RefreshHz = 60,
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
          RefreshHz = 144,
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

      using var reader = new CaptureFileReader(result.FramesPath);
      var image = reader.CreateFrameImage();
      var decoder = new MarkerDecoder();
      var o = scenario.Options;
      int size = MarkerRenderer.MarkerSizePx(o.ModuleSizePx);
      var markerLock = new MarkerLock(new PixelRect(o.OriginX, o.OriginY, size, size), o.ModuleSizePx);
      for (long i = 0; i < reader.RecordCount; ++i)
      {
        var header = reader.ReadRecord(i, image);
        Assert.That(header.CaptureIndex, Is.EqualTo(i));
        Assert.That(header.DeviceTicks, Is.EqualTo(scenario.CaptureTicks(i)));
        var expected = scenario.PresentedFrames[scenario.PresentedIndexAt(i)].Payload;
        var decoded = decoder.DecodeLocked(image, markerLock);
        Assert.That(decoded.Payload, Is.EqualTo(expected), $"record {i}");
        if (expected.Kind == MarkerKind.SequenceStart)
          Assert.That(decoded.Start, Is.EqualTo(scenario.StartMetadata));
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
          RefreshHz = 60,
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
      using var reader = new CaptureFileReader(result.FramesPath);
      long first = reader.ReadRecordHeader(0).CaptureIndex;
      long last = reader.ReadRecordHeader(reader.RecordCount - 1).CaptureIndex;
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

      using var reader = new CaptureFileReader(result.FramesPath);
      long first = reader.ReadRecordHeader(0).CaptureIndex;
      long last = reader.ReadRecordHeader(reader.RecordCount - 1).CaptureIndex;
      Assert.That(first, Is.EqualTo(startCapture - 16), "16 frames of pre-roll before the start marker");
      Assert.That(last, Is.EqualTo(endCapture + 6), "100 ms of end tail at 60 fps, nothing after it");
      Assert.That(reader.RecordCount, Is.EqualTo(last - first + 1), "no frame of the run is missing");
    }
  }
}
