//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which part of the frames a capture stores (RegionRule): a recording's markers by default, the whole frame for live sources, for 'full'
//* and where the frames themselves are wanted; a rectangle or 'auto' when asked. The default falls back to the whole frame when the
//* markers cannot be cropped to, and a recording without a marker stops.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class RegionRuleTests
  {
    private static readonly FfmpegCaptureOptions g_recording = new FfmpegCaptureOptions
    {
      FfmpegPath = "ffmpeg",
      Device = new CaptureDevice(FfmpegInputKind.Media, "recording.mkv", "recording.mkv"),
      InputPath = "recording.mkv",
    };

    private static readonly FfmpegCaptureOptions g_live = new FfmpegCaptureOptions
    {
      FfmpegPath = "ffmpeg",
      Device = new CaptureDevice(FfmpegInputKind.DirectShow, "Cam Link 4K", "Cam Link 4K"),
    };

    // A 1080p source with 6 px modules: the main marker top left, the sync marker bottom left
    private static readonly MarkerLock g_main = MarkerLock.At(32, 32, 6);
    private static readonly MarkerLock g_sync = MarkerLock.At(32, 850, 6, MarkerKind.Sync);

    private static MarkerLocateResult Located(MarkerLock? sync) =>
      new MarkerLocateResult(g_main, MarkerCrop.For(g_main, sync, 1920, 1080), 1920, 1080) { SyncLock = sync };

    [TestCase(null, false, true, false, RegionChoice.MarkersIfTheyFit, Description = "a recording, nothing asked")]
    [TestCase("  ", false, true, false, RegionChoice.MarkersIfTheyFit, Description = "an empty box is nothing asked")]
    [TestCase(null, true, true, false, RegionChoice.WholeFrame, Description = "a stored size: the frame scaled")]
    [TestCase(null, false, true, true, RegionChoice.WholeFrame, Description = "the frames are kept: the whole frames")]
    [TestCase(null, false, false, false, RegionChoice.WholeFrame, Description = "a live source")]
    [TestCase("full", false, true, false, RegionChoice.WholeFrame)]
    [TestCase(" Full ", false, true, false, RegionChoice.WholeFrame)]
    [TestCase("auto", false, false, true, RegionChoice.Markers, Description = "asked for: also live, also with the frames kept")]
    [TestCase("AUTO", true, true, false, RegionChoice.Markers)]
    [TestCase("8,16,320,320", false, true, false, RegionChoice.Rectangle)]
    [TestCase("8,16,320,320", true, false, true, RegionChoice.Rectangle)]
    public void For_ChoosesWhatIsStored(string? roi, bool hasScale, bool recording, bool keepFrames, RegionChoice expected)
    {
      Assert.That(RegionRule.For(roi, hasScale, recording, keepFrames), Is.EqualTo(expected));
    }

    [Test]
    public void ARecording_StoresItsMarkersRegions_OneOrTwo()
    {
      var notes = new List<string>();
      var one = RegionRule.Apply(g_recording, null, null, false, () => Located(null), notes.Add);
      var two = RegionRule.Apply(g_recording, null, null, false, () => Located(g_sync), notes.Add);

      Assert.That(notes, Is.Empty);
      Assert.That(
        (one.Roi, one.SyncRoi, one.Scale, one.RoiDownscale),
        Is.EqualTo((Located(null).Crop.Roi, (PixelRect?)null, ((int, int)?)(165, 165), 1))
      );
      var crop = Located(g_sync).Crop;
      Assert.That(crop.HasSyncRoi, Is.True);
      Assert.That((two.Roi, two.SyncRoi, two.Scale, two.RoiDownscale), Is.EqualTo((crop.Roi, (PixelRect?)crop.SyncRoi, ((int, int)?)null, 2)));
    }

    [Test]
    public void ARecordingWhoseMarkersCannotBeCroppedTo_StoresTheWholeFrame_AndSaysWhy()
    {
      var notes = new List<string>();
      var options = RegionRule.Apply(
        g_recording,
        null,
        null,
        false,
        () => throw new MarkerRegionException("The marker moved between frames."),
        notes.Add
      );

      Assert.That((options.Roi, options.SyncRoi, options.Scale), Is.EqualTo(((PixelRect?)null, (PixelRect?)null, ((int, int)?)null)));
      Assert.That(notes, Has.Count.EqualTo(1));
      Assert.That(notes[0], Does.Contain("whole frame").And.Contain("The marker moved between frames."));
    }

    [Test]
    public void ARecordingWithoutAMarker_Stops()
    {
      Assert.Throws<MarkerNotFoundException>(() =>
        RegionRule.Apply(g_recording, null, null, false, () => throw new MarkerNotFoundException("No marker was found in 20 frames"), _ => { })
      );
    }

    [Test]
    public void AutoThatCannotBeCroppedTo_IsAnError_AndTakesNoStoredSize()
    {
      Assert.Throws<MarkerRegionException>(() =>
        RegionRule.Apply(g_live, "auto", null, false, () => throw new MarkerRegionException("moved"), _ => { })
      );
      Assert.Throws<ArgumentException>(() => RegionRule.Apply(g_live, "auto", (960, 540), false, () => Located(null), _ => { }));
    }

    [Test]
    public void TheWholeFrameAndARectangle_NeedNoLocating()
    {
      MarkerLocateResult Never() => throw new AssertionException("nothing to locate");

      var full = RegionRule.Apply(g_recording, "full", null, false, Never, _ => { });
      var scaled = RegionRule.Apply(g_recording, null, (960, 540), false, Never, _ => { });
      var kept = RegionRule.Apply(g_recording, null, null, true, Never, _ => { });
      var live = RegionRule.Apply(g_live, null, null, false, Never, _ => { });
      var rectangle = RegionRule.Apply(g_live, "8,16,320,320", (160, 160), false, Never, _ => { });

      foreach (var whole in new[] { full, kept, live })
        Assert.That((whole.Roi, whole.SyncRoi, whole.Scale), Is.EqualTo(((PixelRect?)null, (PixelRect?)null, ((int, int)?)null)));
      Assert.That((scaled.Roi, scaled.Scale), Is.EqualTo(((PixelRect?)null, ((int, int)?)(960, 540))));
      Assert.That(
        (rectangle.Roi, rectangle.SyncRoi, rectangle.Scale),
        Is.EqualTo(((PixelRect?)new PixelRect(8, 16, 320, 320), (PixelRect?)null, ((int, int)?)(160, 160)))
      );
    }
  }
}
