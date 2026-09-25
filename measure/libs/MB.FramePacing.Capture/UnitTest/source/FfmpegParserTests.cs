//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* ffmpeg output parsing and command construction, using captured ffmpeg output as fixtures.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class FfmpegParserTests
  {
    [Test]
    public void StderrParser_ReadsStreamsTimeBaseAndFrames()
    {
      var parser = new FfmpegStderrParser();
      string[] lines =
      [
        "Input #0, dshow, from 'video=Cam Link 4K':",
        "  Duration: N/A, start: 12345.678900, bitrate: N/A",
        "  Stream #0:0: Video: rawvideo (YUY2 / 0x32595559), yuyv422(tv, bt709), 1920x1080, 59.94 fps, 59.94 tbr, 10000k tbn",
        "Stream mapping:",
        "  Stream #0:0 -> #0:0 (rawvideo (native) -> rawvideo (native))",
        "[Parsed_showinfo_2 @ 000001f2] config in time_base: 1/10000000, frame_rate: 60000/1001",
        "[Parsed_showinfo_2 @ 000001f2] config out time_base: 0/0, frame_rate: 0/0",
        "Output #0, rawvideo, to 'pipe:1':",
        "  Stream #0:0: Video: rawvideo (Y800 / 0x30303859), gray(pc, progressive), 960x540, q=2-31, 248832 kb/s, 59.94 fps, 59.94 tbn",
        "[Parsed_showinfo_2 @ 000001f2] n:   0 pts:123456789000 pts_time:12345.7 duration:166833 fmt:gray",
        "[Parsed_showinfo_2 @ 000001f2] n:   1 pts:123456955833 pts_time:12345.7 duration:166833 fmt:gray",
        "[dshow @ 000001f2] real-time buffer [Cam Link 4K] [video input] too full or near too full (101% of size: 3041280 [rtbufsize parameter])! frame dropped!",
      ];
      foreach (var line in lines)
        parser.ProcessLine(line);

      Assert.That(parser.Input, Is.EqualTo(new VideoStreamInfo(1920, 1080, 59.94)));
      Assert.That(parser.Output, Is.EqualTo(new VideoStreamInfo(960, 540, 59.94)));
      Assert.That(parser.OutputKnown.WaitOne(0), Is.True);
      Assert.That(parser.DroppedFrames, Is.EqualTo(1));
      Assert.That(parser.LastFrameNumber, Is.EqualTo(1));
      Assert.That(parser.TryGetDeviceTicks(0, out long first), Is.True);
      Assert.That(first, Is.EqualTo(123456789000L));
      Assert.That(parser.TryGetDeviceTicks(1, out long second), Is.True);
      Assert.That(second, Is.EqualTo(123456955833L));
      Assert.That(parser.TryGetDeviceTicks(0, out _), Is.False, "a timestamp is handed out once");
    }

    [TestCase(90000L, 1L, 90000L, 10_000_000L)]
    [TestCase(1L, 1L, 1000000L, 10L)]
    [TestCase(3L, 1001L, 60000L, 500_500L)]
    [TestCase(-90000L, 1L, 90000L, -10_000_000L)]
    [TestCase(long.MaxValue / 2, 1L, 10_000_000L, long.MaxValue / 2)]
    public void PtsToTicks(long pts, long numerator, long denominator, long expected)
    {
      Assert.That(FfmpegStderrParser.PtsToTicks(pts, numerator, denominator), Is.EqualTo(expected));
    }

    [Test]
    public void DirectShowDevices_NewAndLegacyFormats()
    {
      string[] modern =
      [
        "[dshow @ 000001] \"Cam Link 4K\" (video)",
        "[dshow @ 000001]   Alternative name \"@device_pnp_\\\\?\\usb#vid_0fd9&pid_0066\"",
        "[dshow @ 000001] \"OBS Virtual Camera\" (none)",
        "[dshow @ 000001] \"Microphone (Cam Link 4K)\" (audio)",
      ];
      var devices = FfmpegDeviceParser.ParseDirectShowDevices(modern);
      Assert.That(devices, Has.Count.EqualTo(1));
      Assert.That(devices[0].Input, Is.EqualTo("Cam Link 4K"));

      string[] legacy =
      [
        "[dshow @ 000002] DirectShow video devices (some may be both video and audio devices)",
        "[dshow @ 000002]  \"USB Capture HDMI 4K+\"",
        "[dshow @ 000002]     Alternative name \"@device_pnp_xyz\"",
        "[dshow @ 000002] DirectShow audio devices",
        "[dshow @ 000002]  \"Line In\"",
      ];
      devices = FfmpegDeviceParser.ParseDirectShowDevices(legacy);
      Assert.That(devices, Has.Count.EqualTo(1));
      Assert.That(devices[0].Name, Is.EqualTo("USB Capture HDMI 4K+"));
    }

    [Test]
    public void DirectShowModes()
    {
      string[] lines =
      [
        "[dshow @ 000001] DirectShow video device options (from video devices)",
        "[dshow @ 000001]  Pin \"Capture\" (alternative pin name \"0\")",
        "[dshow @ 000001]   pixel_format=yuyv422  min s=1920x1080 fps=59.9402 max s=1920x1080 fps=60.0002",
        "[dshow @ 000001]   vcodec=mjpeg  min s=1920x1080 fps=5 max s=1920x1080 fps=240 (tv, bt470bg/bt709/unknown, topleft)",
        "[dshow @ 000001]   pixel_format=nv12  min s=1280x720 fps=5 max s=1280x720 fps=120",
      ];
      var modes = FfmpegDeviceParser.ParseDirectShowModes(lines);
      Assert.That(modes, Has.Count.EqualTo(3));
      Assert.That(modes[1], Is.EqualTo(new CaptureMode(1920, 1080, 240, "mjpeg", true)));
      Assert.That(modes[2], Is.EqualTo(new CaptureMode(1280, 720, 120, "nv12", false)));
    }

    [Test]
    public void AVFoundationDevices()
    {
      string[] lines =
      [
        "[AVFoundation indev @ 0x7f] AVFoundation video devices:",
        "[AVFoundation indev @ 0x7f] [0] UltraStudio Recorder 3G",
        "[AVFoundation indev @ 0x7f] [1] Capture screen 0",
        "[AVFoundation indev @ 0x7f] AVFoundation audio devices:",
        "[AVFoundation indev @ 0x7f] [0] MacBook Pro Microphone",
      ];
      var devices = FfmpegDeviceParser.ParseAVFoundationDevices(lines);
      Assert.That(devices, Has.Count.EqualTo(2));
      Assert.That(devices[0], Is.EqualTo(new CaptureDevice(FfmpegInputKind.AVFoundation, "0", "UltraStudio Recorder 3G")));
    }

    [Test]
    public void Video4Linux2Modes()
    {
      string[] lines =
      [
        "[video4linux2,v4l2 @ 0x55] Raw       :     yuyv422 :           YUYV 4:2:2 : 640x480 1280x720 1920x1080",
        "[video4linux2,v4l2 @ 0x55] Compressed:       mjpeg :          Motion-JPEG : 1920x1080",
      ];
      var modes = FfmpegDeviceParser.ParseVideo4Linux2Modes(lines);
      Assert.That(modes, Has.Count.EqualTo(4));
      Assert.That(modes[2], Is.EqualTo(new CaptureMode(1920, 1080, 0, "yuyv422", false)));
      Assert.That(modes[3], Is.EqualTo(new CaptureMode(1920, 1080, 0, "mjpeg", true)));
    }

    [Test]
    public void CaptureCommand_DirectShowWithCropAndScale()
    {
      var options = new FfmpegCaptureOptions
      {
        FfmpegPath = "ffmpeg",
        Device = new CaptureDevice(FfmpegInputKind.DirectShow, "Cam Link 4K", "Cam Link 4K"),
        Mode = RequestedMode.Parse("1920x1080@240"),
        InputFormat = "mjpeg",
        Roi = new PixelRect(0, 0, 960, 540),
        Scale = (480, 270),
      };
      var args = string.Join(" ", FfmpegCommandBuilder.BuildCapture(options));
      Assert.That(args, Does.Contain("-f dshow -rtbufsize 1024M -video_size 1920x1080 -framerate 240 -vcodec mjpeg -i video=Cam Link 4K"));
      Assert.That(args, Does.Contain("-copyts -fps_mode passthrough -vf crop=960:540:0:0:exact=1,scale=480:270:flags=area,format=gray,showinfo"));
      Assert.That(args, Does.EndWith("-f rawvideo -pix_fmt gray pipe:1"));
    }

    [Test]
    public void CaptureCommand_Video4Linux2AndAVFoundation()
    {
      var v4l2 = new FfmpegCaptureOptions
      {
        FfmpegPath = "ffmpeg",
        Device = new CaptureDevice(FfmpegInputKind.Video4Linux2, "/dev/video2", "cap"),
        InputFormat = "yuyv422",
        Mode = RequestedMode.Parse("@120"),
      };
      Assert.That(
        string.Join(" ", FfmpegCommandBuilder.BuildCapture(v4l2)),
        Does.Contain("-f v4l2 -input_format yuyv422 -framerate 120 -i /dev/video2")
      );

      var avf = new FfmpegCaptureOptions { FfmpegPath = "ffmpeg", Device = new CaptureDevice(FfmpegInputKind.AVFoundation, "1", "cap") };
      Assert.That(string.Join(" ", FfmpegCommandBuilder.BuildCapture(avf)), Does.Contain("-f avfoundation -i 1:none"));
    }

    [TestCase("1920x1080@240", 1920, 1080, 240.0)]
    [TestCase("1280x720", 1280, 720, 0.0)]
    [TestCase("@59.94", 0, 0, 59.94)]
    public void RequestedMode_Parse(string text, int width, int height, double fps)
    {
      Assert.That(RequestedMode.Parse(text), Is.EqualTo(new RequestedMode(width, height, fps)));
    }

    [TestCase("1920x")]
    [TestCase("axb@60")]
    [TestCase("1920x1080@-1")]
    public void RequestedMode_RejectsGarbage(string text)
    {
      Assert.Throws<FormatException>(() => RequestedMode.Parse(text));
    }
  }
}
