//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The test clips in test-data/videos: where they are, their names, finding ffmpeg (tests are ignored without it) and importing a clip as a
//* capture through the real import path. Shared by the VideoClip tests of the analysis and of the charts.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  public static class VideoClips
  {
    /// <summary>The clip folder names, for TestCaseSource.</summary>
    public static IEnumerable<string> Names() =>
      System.IO.Directory.GetDirectories(Directory()).Where(d => File.Exists(Path.Combine(d, "video.mp4"))).Select(Path.GetFileName).Order()!;

    public static string Directory()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !System.IO.Directory.Exists(Path.Combine(directory.FullName, "test-data", "videos")))
        directory = directory.Parent;
      return directory != null
        ? Path.Combine(directory.FullName, "test-data", "videos")
        : throw new DirectoryNotFoundException("Could not locate test-data/videos above " + TestContext.CurrentContext.TestDirectory);
    }

    public static ClipManifest Manifest(string clip) => ClipManifest.Load(Path.Combine(Directory(), clip));

    /// <summary>The ffmpeg executable; ignores the test when there is none.</summary>
    public static string FindFfmpegOrIgnore()
    {
      try
      {
        return FfmpegLocator.Find(null, FramePacingConfig.Load());
      }
      catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
      {
        Assert.Ignore("ffmpeg is not installed: " + ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Imports the clip's video as a capture into <paramref name="output"/> (the capture data, and the frames too with
    /// <paramref name="keepFrames"/>) and returns it.
    /// </summary>
    public static string Import(string clip, string ffmpeg, string output, bool keepFrames = false)
    {
      var media = MediaInput.Create(Path.Combine(Directory(), clip, "video.mp4"), new MediaInputOptions(), output);
      using (var source = FfmpegCaptureSource.Start(media.ToCaptureOptions(ffmpeg), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output, KeepFrames = keepFrames }, null, CancellationToken.None);
      return output;
    }
  }
}
