//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The test clips in measure/test-data/videos: where they are, their names, finding ffmpeg (tests are ignored without it) and importing a clip as a
//* capture through the real import path. Shared by the VideoClip tests of the analysis and of the charts. A clip is imported once for a test
//* run and every test gets a copy of that capture (Imported): an import starts ffmpeg and decodes every frame's marker, many tests look
//* at the same clip, and they run at once.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Concurrent;
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
    // The clips imported for this test run, each once, in a folder of its own that is deleted when the test process ends. Nothing is
    // analysed there: an analysis writes its output next to the capture, and tests run at once
    private static readonly ConcurrentDictionary<string, Lazy<string>> g_imported = new ConcurrentDictionary<string, Lazy<string>>();
    private static readonly string g_importedRoot = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", "clips-" + Guid.NewGuid().ToString("N"));

    static VideoClips()
    {
      AppDomain.CurrentDomain.ProcessExit += (_, _) =>
      {
        try
        {
          if (System.IO.Directory.Exists(g_importedRoot))
            System.IO.Directory.Delete(g_importedRoot, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      };
    }

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
        : throw new DirectoryNotFoundException("Could not locate measure/test-data/videos above " + TestContext.CurrentContext.TestDirectory);
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
    /// The clip as a capture in <paramref name="directory"/> (its capture data, not its frames): a copy of the capture the clip was
    /// imported as once for the test run. The copy is the test's own, to analyse and write next to. A test of the import itself uses
    /// <see cref="Import"/>.
    /// </summary>
    public static string Imported(string clip, string ffmpeg, string directory)
    {
      string source = g_imported.GetOrAdd(clip, name => new Lazy<string>(() => Import(name, ffmpeg, Path.Combine(g_importedRoot, name)))).Value;
      System.IO.Directory.CreateDirectory(directory);
      foreach (string file in System.IO.Directory.GetFiles(source))
        File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
      return directory;
    }

    /// <summary>
    /// Imports the clip's video as a capture into <paramref name="output"/> (the capture data, and the frames too with
    /// <paramref name="keepFrames"/>) and returns it.
    /// </summary>
    public static string Import(string clip, string ffmpeg, string output, bool keepFrames = false)
    {
      var media = MediaInput.Create(Path.Combine(Directory(), clip, "video.mp4"), new MediaInputOptions(), output);
      using (var source = FfmpegCaptureSource.Start(media.ToCaptureOptions(ffmpeg), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(
          source,
          new CaptureRunOptions
          {
            OutputDirectory = output,
            KeepFrames = keepFrames,
            InputPath = media.InputPath,
          },
          null,
          CancellationToken.None
        );
      return output;
    }
  }
}
