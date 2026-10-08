//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Image sequence and media input: ordering, timestamp files, the ffconcat list and the ffmpeg arguments.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
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
      Assert.That(frames.Select(f => f.Time.Nanoseconds), Is.EqualTo(new long[] { 0, 4_000_000, 8_000_000, 12_000_000 }));
    }

    [Test]
    public void Collect_TimestampFile_DefinesOrderAndTimes()
    {
      using var temp = new TempDirectory();
      Touch(temp, "a.png", "b.png", "c.png");
      var csv = temp.File("times.csv");
      File.WriteAllText(csv, "fileName,timeNs\n# comment\nc.png,100000000\na.png,104500000\n\nb.png,110000000\n");

      var frames = ImageSequence.Collect(temp.Path, null, csv);

      Assert.That(frames.Select(f => Path.GetFileName(f.Path)), Is.EqualTo(new[] { "c.png", "a.png", "b.png" }));
      Assert.That(frames.Select(f => f.Time.Nanoseconds), Is.EqualTo(new long[] { 100_000_000, 104_500_000, 110_000_000 }));
    }

    [Test]
    public void Collect_TimestampFile_FindsItsColumnsByName_AndReadsTheNanosecondsAsTheyAre()
    {
      using var temp = new TempDirectory();
      Touch(temp, "a.png", "b.png", "c.png");
      var csv = temp.File("times.csv");
      // A comment and an empty line before the header, the columns in the other order with one more between them, spaces around the
      // cells (the file is written by hand), a negative first time, and a time a double does not hold exactly (2^53 + 1)
      File.WriteAllText(
        csv,
        "# exported by the camera\r\n\r\ntimeNs,exposureUs,fileName\r\n-5 , 100 , a.png\r\n9007199254740993,100,b.png\r\n9223372036854775807,100,c.png\r\n"
      );

      var frames = ImageSequence.Collect(temp.Path, null, csv);

      Assert.That(frames.Select(f => Path.GetFileName(f.Path)), Is.EqualTo(new[] { "a.png", "b.png", "c.png" }));
      Assert.That(frames.Select(f => f.Time.Nanoseconds), Is.EqualTo(new long[] { -5, 9_007_199_254_740_993, long.MaxValue }));
    }

    [TestCase("c.png,100\n", 1, "timeNs", TestName = "{m}(no header: the oldest file's first line)")]
    [TestCase("file,timeMs\nc.png,100\n", 1, "1 000 000", TestName = "{m}(the oldest header)")]
    [TestCase("fileName,timeMs\nc.png,100\n", 1, "1 000 000", TestName = "{m}(milliseconds)")]
    [TestCase("fileName,timeTicks\nc.png,100\n", 1, "timeTicks", TestName = "{m}(ticks of 100 ns: the header from before the nanoseconds)")]
    [TestCase("fileName,timeTicks\nc.png,100\n", 1, "by 100 or", TestName = "{m}(ticks of 100 ns: what to do)")]
    [TestCase("fileName\nc.png\n", 1, "timeNs", TestName = "{m}(no time column)")]
    [TestCase("timeNs\n100\n", 1, "fileName", TestName = "{m}(no file name column)")]
    [TestCase("fileName,timeNs\nc.png,104.5\n", 2, "104.5", TestName = "{m}(a fraction)")]
    [TestCase("fileName,timeNs\nc.png,0\na.png,1e3\n", 3, "1e3", TestName = "{m}(an exponent)")]
    [TestCase("fileName,timeNs\nc.png,+5\n", 2, "+5", TestName = "{m}(a plus sign)")]
    [TestCase("fileName,timeNs\nc.png,1_000\n", 2, "1_000", TestName = "{m}(a digit separator)")]
    [TestCase("fileName,timeNs\nc.png,\n", 2, "whole number", TestName = "{m}(an empty time)")]
    [TestCase("fileName,timeNs\n# two images\n\nc.png\n", 4, "whole number", TestName = "{m}(a line with one cell)")]
    [TestCase("fileName,timeNs\nc.png,9223372036854775808\n", 2, "9223372036854775808", TestName = "{m}(more than 64 bits hold)")]
    [TestCase("fileName,timeNs\n,5\n", 2, "file name", TestName = "{m}(no file name)")]
    public void Collect_TimestampFile_ThatIsNotOne_IsRefused(string content, int line, string says)
    {
      using var temp = new TempDirectory();
      Touch(temp, "a.png", "c.png");
      var csv = temp.File("times.csv");
      File.WriteAllText(csv, content);

      Assert.That(
        () => ImageSequence.Collect(temp.Path, null, csv),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains($"times.csv:{line}: ").And.Message.Contains(says)
      );
    }

    [Test]
    public void Collect_Errors()
    {
      using var temp = new TempDirectory();
      Assert.Throws<DirectoryNotFoundException>(() => ImageSequence.Collect(temp.File("missing"), 60, null));
      Assert.Throws<ArgumentException>(() => ImageSequence.Collect(temp.Path, null, null), "a frame rate or timestamps are required");
      Assert.Throws<FileNotFoundException>(() => ImageSequence.Collect(temp.Path, 60, null), "no images");

      var csv = temp.File("times.csv");
      File.WriteAllText(csv, "fileName,timeNs\nmissing.png,0\n");
      Assert.Throws<FileNotFoundException>(() => ImageSequence.Collect(temp.Path, null, csv));
      File.WriteAllText(csv, "fileName,timeNs\n# no images\n");
      Assert.That(
        () => ImageSequence.Collect(temp.Path, null, csv),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("lists no images")
      );
      File.WriteAllText(csv, string.Empty);
      Assert.That(() => ImageSequence.Collect(temp.Path, null, csv), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("timeNs"));
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
      var frames = new[]
      {
        new ImageSequenceFrame(temp.File("a.png"), new NanosecondTickCount(100)),
        new ImageSequenceFrame(temp.File("b.png"), new NanosecondTickCount(100)),
      };
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
      Assert.That(sequence.FrameTimestamps, Is.EqualTo(new[] { new NanosecondTickCount(0), new NanosecondTickCount(8_333_333) }));
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
