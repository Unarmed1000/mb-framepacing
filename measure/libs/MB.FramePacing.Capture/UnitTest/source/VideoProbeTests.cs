//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What ffmpeg's description of a video file says (FfmpegVideoProbe.Parse), whether browsers play it (VideoCodecInfo), and the playable copy
//* made of one they do not (FfmpegCommandBuilder.BuildPlayableCopy: a remux when the stream plays, else an H.264 transcode; every frame and
//* timestamp kept either way).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Capture.Ffmpeg;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class VideoProbeTests
  {
    private static VideoCodecInfo Probe(string file, string container, string stream) =>
      FfmpegVideoProbe.Parse(
        new[]
        {
          $"Input #0, {container}, from '{file}':",
          "  Metadata:",
          "    encoder         : Lavf61.7.100",
          "  Duration: 00:01:02.50, start: 0.000000, bitrate: 12000 kb/s",
          $"  Stream #0:0[0x1](und): Video: {stream}",
          "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 160 kb/s (default)",
          "At least one output file must be specified",
        },
        file
      )!;

    [Test]
    public void Parse_AnObsRecording_IsPlayable()
    {
      var info = Probe(
        @"D:\recordings\capture.mp4",
        "mov,mp4,m4a,3gp,3g2,mj2",
        "h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 11800 kb/s, 59.94 fps, 59.94 tbr, 60k tbn (default)"
      );

      Assert.That(info.Extension, Is.EqualTo("mp4"));
      Assert.That(info.Codec, Is.EqualTo("h264"));
      Assert.That(info.Profile, Is.EqualTo("High"));
      Assert.That(info.PixelFormat, Is.EqualTo("yuv420p"));
      Assert.That(info.Fps, Is.EqualTo(59.94));
      Assert.That(info.Duration, Is.EqualTo(new TimeSpan(0, 0, 1, 2, 500)));
      Assert.That(info.Playable, Is.True);
      Assert.That(info.Problem, Is.Null);
      Assert.That(info.Description, Is.EqualTo("h264 (High), yuv420p, mp4"));
      Assert.That(info.KeyframeInterval, Is.EqualTo(60));
    }

    [Test]
    public void Parse_H264InMatroska_NeedsOnlyARemux()
    {
      var info = Probe(
        "capture.mkv",
        "matroska,webm",
        "h264 (High), yuv420p(tv, bt709, progressive), 2560x1440, SAR 1:1 DAR 16:9, 240 fps, 240 tbr, 1k tbn (default)"
      );

      Assert.That(info.Playable, Is.False);
      Assert.That(info.CodecPlayable, Is.True);
      Assert.That(info.RemuxIsEnough, Is.True);
      Assert.That(info.Problem, Does.Contain(".mkv"));
    }

    [TestCase("h264 (High 4:4:4 Predictive) (avc1 / 0x31637661), yuv444p(progressive), 1280x720, 60 fps", "mp4")]
    [TestCase("h264 (High 10) (avc1 / 0x31637661), yuv420p10le(progressive), 1280x720, 60 fps", "mp4")]
    [TestCase("hevc (Main) (hvc1 / 0x31637668), yuv420p(tv, progressive), 3840x2160, 60 fps", "mp4")]
    [TestCase("utvideo (ULY2 / 0x32594C55), yuv422p, 1920x1080, 60 fps", "avi")]
    [TestCase("rawvideo (BGR[24] / 0x18524742), bgr24, 1920x1080, 60 fps", "avi")]
    public void Parse_LosslessHevcAndRaw_AreNotPlayable_AndNeedATranscode(string stream, string extension)
    {
      var info = Probe("capture." + extension, extension == "mp4" ? "mov,mp4,m4a,3gp,3g2,mj2" : "avi", stream);

      Assert.That(info.Playable, Is.False);
      Assert.That(info.RemuxIsEnough, Is.False);
      Assert.That(info.Problem, Does.StartWith("browsers cannot play " + info.Codec));
    }

    [TestCase("vp9 (Profile 0), yuv420p(tv, progressive), 1920x1080, SAR 1:1 DAR 16:9, 60 fps", "webm", "matroska,webm", true)]
    [TestCase("vp8, yuv420p(progressive), 1920x1080, 60 fps", "webm", "matroska,webm", true)]
    [TestCase("av1 (Main) (av01 / 0x31307661), yuv420p10le(tv, progressive), 1920x1080, 60 fps", "mp4", "mov,mp4,m4a,3gp,3g2,mj2", true)]
    [TestCase("vp8, yuv420p(progressive), 1920x1080, 60 fps", "mp4", "mov,mp4,m4a,3gp,3g2,mj2", false)]
    public void Playable_FollowsTheCodecAndTheContainer(string stream, string extension, string container, bool playable)
    {
      Assert.That(Probe("capture." + extension, container, stream).Playable, Is.EqualTo(playable));
    }

    [Test]
    public void Parse_WithoutAVideoStream_IsNull()
    {
      Assert.That(
        FfmpegVideoProbe.Parse(new[] { "Input #0, wav, from 'sound.wav':", "  Stream #0:0: Audio: pcm_s16le, 48000 Hz, 2 channels" }, "sound.wav"),
        Is.Null
      );
    }

    [Test]
    public void PlayableCopy_RemuxesAPlayableStream_AndTranscodesTheRest_KeepingEveryTimestamp()
    {
      var remux = FfmpegCommandBuilder.BuildPlayableCopy("in.mkv", "out.mp4", Probe("in.mkv", "matroska,webm", "h264 (High), yuv420p, 60 fps"));
      var transcode = FfmpegCommandBuilder.BuildPlayableCopy(
        "in.mp4",
        "out.mp4",
        Probe("in.mp4", "mov,mp4,m4a,3gp,3g2,mj2", "h264 (High 4:4:4 Predictive) (avc1 / 0x31637661), yuv444p, 1280x720, 60 fps")
      );

      foreach (var args in new[] { remux, transcode })
      {
        Assert.That(string.Join(" ", args), Does.Contain("-map 0:v:0 -an -sn -dn -copyts -avoid_negative_ts disabled"));
        Assert.That(args[^1], Is.EqualTo("out.mp4"));
      }
      Assert.That(string.Join(" ", remux), Does.Contain("-c:v copy"));
      Assert.That(remux, Does.Not.Contain("libx264"));
      string transcoded = string.Join(" ", transcode);
      Assert.That(transcoded, Does.Contain("-fps_mode passthrough"));
      Assert.That(transcoded, Does.Contain("-c:v libx264"));
      Assert.That(transcoded, Does.Contain("format=yuv420p"));
      Assert.That(transcoded, Does.Contain("-bf 0"));
      Assert.That(transcoded, Does.Contain("-g 60"));
    }

    [TestCase("out_time_us=1500000", 15_000_000L)]
    [TestCase("out_time_us=0", 0L)]
    [TestCase("out_time_us=N/A", null)]
    [TestCase("frame=12", null)]
    public void PlayableCopy_ReadsItsProgress(string line, long? ticks)
    {
      Assert.That(PlayableCopy.ProgressTime(line)?.Ticks, Is.EqualTo(ticks));
    }
  }
}
