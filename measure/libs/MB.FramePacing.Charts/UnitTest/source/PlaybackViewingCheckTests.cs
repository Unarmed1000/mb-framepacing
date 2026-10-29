//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The playback page's viewing check, read from the page as a headless Edge or Chrome leaves it: a display at a whole multiple of the
//* recording's rate shows every video frame for the same number of refreshes, one a little off drifts against it now and then, and any other
//* rate is a warning that says what the viewer sees. The display's rate is given (#display-hz), as a headless browser's is its machine's.
//* The page is the golden run's (no ffmpeg needed), a 60 Hz capture. Skipped where there is no browser.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Charts.Playback;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  [NonParallelizable] // starts browsers, which abort or hang now and then when several start at once
  public class PlaybackViewingCheckTests
  {
    private static readonly PlaybackVideo g_video = new PlaybackVideo(
      PlaybackVideoKind.Copied,
      "video.mp4",
      "recording.mp4",
      1,
      DateTime.UnixEpoch,
      true,
      "h264 (High), yuv420p, mp4",
      null
    );

    private string m_directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        if (Directory.Exists(m_directory))
          Directory.Delete(m_directory, true);
      }
      catch (IOException) { }
    }

    [TestCase("60", "ok", "Display 60 Hz, recording 60 Hz: each video frame for one refresh, as captured.")]
    [TestCase("120", "ok", "Display 120 Hz, recording 60 Hz: each video frame for 2 refreshes, as captured.")]
    [TestCase("240", "ok", "Display 240 Hz, recording 60 Hz: each video frame for 4 refreshes, as captured.")]
    [TestCase("59.94", "ok", "Display 59.94 Hz, recording 60 Hz: each video frame for one refresh, but about every 17 s one is skipped")]
    [TestCase("50", "warn", "Display 50 Hz, recording 60 Hz: one video frame in 6 is never shown, so the video judders where the capture did not")]
    [TestCase("75", "warn", "Display 75 Hz, recording 60 Hz: one video frame in 4 is on screen a refresh longer than the others")]
    [TestCase("144", "warn", "Display 144 Hz, recording 60 Hz: 40 % of the video frames are on screen a refresh longer than the others")]
    public void ViewingCheck_SaysWhatTheDisplayDoesWithTheVideo(string displayHz, string status, string text)
    {
      if (HeadlessBrowser.Find() is not { } browser)
      {
        Assert.Ignore("No Edge or Chrome on this machine");
        return;
      }
      string golden = Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "..", "..", "sdk", "test-data", "data", "60-busy-full-rate"));
      var chart = AnalysisOutput.Read(golden).Single().Chart;
      string page = Path.Combine(m_directory, "index.html");
      File.WriteAllText(page, PlaybackPage.Build(RunSection.Whole(chart), g_video));

      string document = HeadlessBrowser.PageDocument(page, "display-hz=" + displayHz, browser: browser);
      var check = Regex.Match(
        document,
        "<div [^>]*id=\"pb-viewing\"[^>]*data-status=\"(\\w+)\"[^>]*>.*?<ul [^>]*>(.*?)</ul>",
        RegexOptions.Singleline
      );
      Assert.That(check.Success, Is.True, "the viewing check");
      var rows = Regex.Matches(check.Groups[2].Value, "<li>(.*?)</li>").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
      Assert.That(check.Groups[1].Value, Is.EqualTo(status), string.Join(Environment.NewLine, rows));
      Assert.That(rows, Has.Count.EqualTo(1));
      Assert.That(rows[0], Does.StartWith(text));
      if (status == "warn")
        Assert.That(rows[0], Does.EndWith("Set the display to 60, 120 or 240 Hz to see it as captured."));
    }
  }
}
