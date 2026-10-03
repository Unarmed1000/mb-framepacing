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
    [TestCase("8,16,320,320+8,700,240,240", true, false, false, RegionChoice.Rectangle, Description = "two rectangles are asked for as one is")]
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

    /// <summary>
    /// What locating the markers found, written down (the region box, 'locate's arguments) and given back, stores what the located
    /// result itself does: one region with its stored size, or both markers' stacked, so tearing is still checked.
    /// </summary>
    [Test]
    public void ALocatedResultWrittenDown_StoresTheSameAgain_WithoutLocating([Values] bool withSync, [Values(3, 6)] int modulePx)
    {
      MarkerLocateResult Never() => throw new AssertionException("nothing to locate");
      var main = MarkerLock.At(32, 32, modulePx);
      MarkerLock? sync = withSync ? MarkerLock.At(32, 850, modulePx, MarkerKind.Sync) : null;
      var located = new MarkerLocateResult(main, MarkerCrop.For(main, sync, 1920, 1080), 1920, 1080) { SyncLock = sync };
      Assert.That(located.Crop.HasSyncRoi, Is.EqualTo(withSync));
      Assert.That(located.Crop.Factor, Is.EqualTo(modulePx / 3));

      foreach (var source in new[] { g_live, g_recording })
      {
        var again = RegionRule.Apply(source, located.RegionText, located.Scale, false, Never, _ => { });
        Assert.That(again, Is.EqualTo(located.Apply(source)));
        Assert.That(FfmpegCommandBuilder.BuildCapture(again), Is.EqualTo(FfmpegCommandBuilder.BuildCapture(located.Apply(source))));
      }
      Assert.That(located.RegionText.Contains('+', StringComparison.Ordinal), Is.EqualTo(withSync));
      string scale = modulePx == 6 ? $" --scale {located.Crop.StoredWidth}x{located.Crop.StoredHeight}" : string.Empty;
      Assert.That(located.Arguments, Is.EqualTo($"--roi {located.RegionText}{scale}"));
    }

    [Test]
    public void TwoRectangles_AreTheMainMarkersRegionThenTheSyncMarkers()
    {
      MarkerLocateResult Never() => throw new AssertionException("nothing to locate");
      var main = new PixelRect(14, 14, 330, 330);
      var sync = new PixelRect(14, 832, 234, 234);

      Assert.That(RegionRule.RegionText(main, sync), Is.EqualTo("14,14,330,330+14,832,234,234"));
      Assert.That(RegionRule.RegionText(main), Is.EqualTo("14,14,330,330"));
      Assert.That(RegionRule.ParseRegions(" 14,14,330,330 + 14,832,234,234 "), Is.EqualTo((main, (PixelRect?)sync)));
      Assert.That(RegionRule.ParseRegions("14,14,330,330"), Is.EqualTo((main, (PixelRect?)null)));

      // Without a stored size the regions are stored as they are; with one, by the whole-number downscale it stands for
      var asTheyAre = RegionRule.Apply(g_live, "14,14,330,330+14,832,234,234", null, false, Never, _ => { });
      var halved = RegionRule.Apply(g_live, "14,14,330,330+14,832,234,234", (165, 282), true, Never, _ => { });
      Assert.That((asTheyAre.Roi, asTheyAre.SyncRoi, asTheyAre.RoiDownscale, asTheyAre.Scale), Is.EqualTo((main, sync, 1, ((int, int)?)null)));
      Assert.That((halved.Roi, halved.SyncRoi, halved.RoiDownscale, halved.Scale), Is.EqualTo((main, sync, 2, ((int, int)?)null)));
      Assert.That(RegionRule.StackedDownscale(main, sync, (55, 94)), Is.EqualTo(6));
    }

    [TestCase("14,14,330,330+14,832,234,234+1,1,8,8", Description = "three")]
    [TestCase("14,14,330,330+", Description = "a second one that is missing")]
    [TestCase("14,14,330,330+auto")]
    [TestCase("14,14,330,330+14,832,234,0", Description = "an empty second region")]
    public void RegionTexts_ThatAreNotOneOrTwoRectangles_AreRefused(string text)
    {
      Assert.Throws<FormatException>(() => RegionRule.Apply(g_live, text, null, false, () => throw new AssertionException("no"), _ => { }));
    }

    [TestCase(160, 282, Description = "not the two regions' width divided by a whole number")]
    [TestCase(165, 280, Description = "another downscale for the height")]
    [TestCase(660, 1128, Description = "larger than the regions")]
    [TestCase(66, 113, Description = "by 5: the sync marker's 234 does not divide")]
    [TestCase(82, 141, Description = "by 4: neither divides")]
    public void TwoRectangles_RefuseAStoredSizeThatIsNoWholeNumberDownscaleOfBoth(int width, int height)
    {
      var exception = Assert.Throws<ArgumentException>(() =>
        RegionRule.Apply(g_live, "14,14,330,330+14,832,234,234", (width, height), false, () => throw new AssertionException("no"), _ => { })
      );
      Assert.That(exception!.Message, Does.Contain("330x564").And.Contain($"{width}x{height}"));
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
