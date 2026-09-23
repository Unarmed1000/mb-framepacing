//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Image sequence and media input: ordering, timestamp files, the ffconcat list and the ffmpeg arguments.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using MB.FramePacing.Capture.Ffmpeg;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class ImageSequenceTests
  {
    private static void Touch(TempDirectory temp, params string[] names)
    {
      foreach (var name in names)
        File.WriteAllText(temp.File(name), string.Empty);
    }

    [Test]
    public void Collect_UsesNaturalOrder_AndTheFrameRate()
    {
      using var temp = new TempDirectory();
      Touch(temp, "frame10.png", "frame2.png", "frame1.png", "notes.txt", "frame0.PNG");

      var frames = ImageSequence.Collect(temp.Path, 250, null);

      Assert.That(frames.Select(f => Path.GetFileName(f.Path)), Is.EqualTo(new[] { "frame0.PNG", "frame1.png", "frame2.png", "frame10.png" }));
      Assert.That(frames.Select(f => f.TimeTicks), Is.EqualTo(new long[] { 0, 40_000, 80_000, 120_000 }));
    }

    [Test]
    public void Collect_TimestampFile_DefinesOrderAndTimes()
    {
      using var temp = new TempDirectory();
      Touch(temp, "a.png", "b.png", "c.png");
      var csv = temp.File("times.csv");
      File.WriteAllText(csv, "file,timeMs\n# comment\nc.png,100\na.png,104.5\nb.png,110\n");

      var frames = ImageSequence.Collect(temp.Path, null, csv);

      Assert.That(frames.Select(f => Path.GetFileName(f.Path)), Is.EqualTo(new[] { "c.png", "a.png", "b.png" }));
      Assert.That(frames.Select(f => f.TimeTicks), Is.EqualTo(new long[] { 1_000_000, 1_045_000, 1_100_000 }));
    }

    [Test]
    public void Collect_Errors()
    {
      using var temp = new TempDirectory();
      Assert.Throws<DirectoryNotFoundException>(() => ImageSequence.Collect(temp.File("missing"), 60, null));
      Assert.Throws<ArgumentException>(() => ImageSequence.Collect(temp.Path, null, null), "a frame rate or timestamps are required");
      Assert.Throws<FileNotFoundException>(() => ImageSequence.Collect(temp.Path, 60, null), "no images");

      var csv = temp.File("times.csv");
      File.WriteAllText(csv, "missing.png,0\n");
      Assert.Throws<FileNotFoundException>(() => ImageSequence.Collect(temp.Path, null, csv));
    }

    [Test]
    public void ConcatList_HasADurationPerImage_AndRepeatsTheLast()
    {
      using var temp = new TempDirectory();
      Touch(temp, "f1.png", "f2.png", "f3.png");
      var frames = ImageSequence.Collect(temp.Path, 100, null);
      var list = temp.File("images.ffconcat");

      double fps = ImageSequence.WriteConcatList(frames, list);

      Assert.That(fps, Is.EqualTo(100).Within(1e-9));
      var lines = File.ReadAllLines(list);
      Assert.That(lines[0], Is.EqualTo("ffconcat version 1.0"));
      Assert.That(lines.Count(l => l.StartsWith("duration 0.01", StringComparison.Ordinal)), Is.EqualTo(3));
      Assert.That(lines[^1], Does.EndWith("f3.png'"));
      Assert.That(lines.Count(l => l.StartsWith("file ", StringComparison.Ordinal)), Is.EqualTo(4));
    }

    [Test]
    public void ConcatList_RejectsTimesThatDoNotIncrease()
    {
      using var temp = new TempDirectory();
      Touch(temp, "a.png", "b.png");
      var frames = new[] { new ImageSequenceFrame(temp.File("a.png"), 100), new ImageSequenceFrame(temp.File("b.png"), 100) };
      Assert.Throws<InvalidDataException>(() => ImageSequence.WriteConcatList(frames, temp.File("list.ffconcat")));
    }

    [Test]
    public void MediaInput_ClassifiesAndBuildsTheRightInput()
    {
      using var temp = new TempDirectory();
      var video = temp.File("clip.mp4");
      File.WriteAllText(video, string.Empty);
      var images = Directory.CreateDirectory(temp.File("images")).FullName;
      File.WriteAllText(Path.Combine(images, "0.png"), string.Empty);
      File.WriteAllText(Path.Combine(images, "1.png"), string.Empty);

      Assert.That(MediaInput.Classify(video), Is.EqualTo(MediaInputKind.VideoFile));
      Assert.That(MediaInput.Classify(images), Is.EqualTo(MediaInputKind.ImageSequence));
      Assert.That(MediaInput.Classify("rtsp://camera/stream"), Is.EqualTo(MediaInputKind.Stream));

      var file = MediaInput.Create(video, new MediaInputOptions(), temp.File("out1"));
      Assert.That(file.Device.Kind, Is.EqualTo(FfmpegInputKind.Media));
      Assert.That(file.Device.IsLive, Is.False);
      Assert.That(file.FrameTimestamps, Is.Null);

      var sequence = MediaInput.Create(images, new MediaInputOptions { Fps = 120 }, temp.File("out2"));
      Assert.That(sequence.Device.Kind, Is.EqualTo(FfmpegInputKind.ImageSequence));
      Assert.That(File.Exists(sequence.Device.Input), Is.True, "the ffconcat list is written into the capture folder");
      Assert.That(sequence.FrameTimestamps, Is.EqualTo(new long[] { 0, 83_333 }));
      Assert.That(sequence.Mode.Fps, Is.EqualTo(120).Within(0.01));

      var stream = MediaInput.Create("srt://127.0.0.1:9000", new MediaInputOptions(), temp.File("out3"));
      Assert.That(stream.Device.IsLive, Is.True);

      Assert.Throws<FileNotFoundException>(() => MediaInput.Create(temp.File("typo.mp4"), new MediaInputOptions(), temp.File("out4")));
    }

    [Test]
    public void CaptureCommand_MediaAndImageSequence()
    {
      var media = new FfmpegCaptureOptions { FfmpegPath = "ffmpeg", Device = new CaptureDevice(FfmpegInputKind.Media, "clip.mp4", "clip") };
      var mediaArgs = string.Join(" ", FfmpegCommandBuilder.BuildCapture(media));
      Assert.That(mediaArgs, Does.Contain("-i clip.mp4"));
      Assert.That(mediaArgs, Does.Not.Contain("-re"), "files are read as fast as possible");

      var sequence = new FfmpegCaptureOptions
      {
        FfmpegPath = "ffmpeg",
        Device = new CaptureDevice(FfmpegInputKind.ImageSequence, "list.ffconcat", "images"),
      };
      Assert.That(string.Join(" ", FfmpegCommandBuilder.BuildCapture(sequence)), Does.Contain("-f concat -safe 0 -i list.ffconcat"));
    }
  }
}
