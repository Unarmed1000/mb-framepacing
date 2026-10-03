//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The playback page of an imported test clip, end to end through ffmpeg: the page holds the report card and its data (the card's plots, the
//* run's frames as the analysis has them), a section's page only the section's frames, and the playable copy keeps every frame of the
//* recording at its timestamp, which the page relies on to find a capture in the video (the copy imported again gives the same capture
//* times). Skipped when ffmpeg is not installed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MB.FramePacing.Analysis;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Charts.Playback;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  public class PlaybackPageTests
  {
    private const string Clip = "60-busy-adaptive";

    private string m_directory = string.Empty;
    private string m_ffmpeg = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_ffmpeg = VideoClips.FindFfmpegOrIgnore();
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

    [Test]
    public async Task Page_HoldsTheReportAndTheRunsFrames_AndAPlayableCopyKeepsEveryTimestamp()
    {
      var report = CaptureAnalyzer.Analyze(VideoClips.Import(Clip, m_ffmpeg, Path.Combine(m_directory, "capture")), new AnalysisOptions());
      var capture = PlaybackCapture.From(report);
      Assert.That(capture.InputPath, Is.EqualTo(Path.Combine(VideoClips.Directory(), Clip, "video.mp4")), "capture.json names the recording");
      Assert.That(capture.Problem(), Is.Null);
      var run = report.Timeline.Runs.Single();
      var chart = ChartRun.From(report, run);
      var runs = new[] { new AnalysisOutputRun(chart, PlaybackExport.PrefixOf(report, run)) };
      var options = new PlaybackExportOptions { FfmpegPath = m_ffmpeg, TranscodeChoice = PlaybackTranscodeChoice.Ask };

      // The clip is lossless 4:4:4 H.264, which browsers do not play: the export asks, and the answer makes a copy
      PlaybackQuestion? asked = null;
      var result = await PlaybackExport.WriteAsync(
        capture,
        runs,
        options,
        question =>
        {
          asked = question;
          return Task.FromResult(true);
        }
      );
      Assert.That(asked?.Title, Does.Contain("playable copy"));
      Assert.That(result.Video.Kind, Is.EqualTo(PlaybackVideoKind.Transcoded));
      Assert.That(result.Pages, Is.EqualTo(new[] { Path.Combine(report.OutputDirectory, "playback", "run-1", "index.html") }));

      string html = File.ReadAllText(result.Pages[0]);
      // The report cards, inline: the whole report shown, the zoom steps (an 8 s clip: 2 s per screen) as text until they are shown
      Assert.That(Regex.Matches(html, "<svg [^>]*role=\"img\"").Count, Is.EqualTo(2), "the report cards, inline");
      Assert.That(html, Does.Contain("<script type=\"text/plain\" id=\"pb-card-source-1\"><svg "));
      Assert.That(html, Does.Not.Contain("<!--PB:").And.Not.Contain("__PB_"), "every placeholder filled");
      Assert.That(html, Does.Not.Contain("<script src").And.Not.Contain("<link "), "nothing loaded from elsewhere");
      using var data = PageData(html);
      var root = data.RootElement;
      Assert.That(root.GetProperty("video").GetProperty("url").GetString(), Is.EqualTo("video.mp4"));
      Assert.That(root.GetProperty("video").GetProperty("playable").GetBoolean(), Is.True);
      Assert.That(root.GetProperty("originTicks").GetInt64(), Is.EqualTo(run.Frames[0].FirstSeenTime.Ticks));

      // The plots are the cards', as the page draws them (its own header has the title and the tiles)
      var cards = root.GetProperty("cards").EnumerateArray().ToList();
      Assert.That(
        cards.Select(c =>
          c.GetProperty("secondsPerScreen").ValueKind == JsonValueKind.Null ? (double?)null : c.GetProperty("secondsPerScreen").GetDouble()
        ),
        Is.EqualTo(new double?[] { null, 2 })
      );
      var card = ReportCard.Build(RunSection.Whole(chart), ReportOptions.Default.Hide(new[] { ReportItem.Title, ReportItem.Tiles }));
      var plots = cards[0].GetProperty("plots").EnumerateArray().ToList();
      Assert.That(plots.Select(p => p.GetProperty("id").GetString()), Is.EqualTo(card.Plots.Select(p => p.Id)));
      Assert.That(plots.Select(p => p.GetProperty("left").GetDouble()), Is.EqualTo(card.Plots.Select(p => p.Left)));
      Assert.That(plots.Select(p => p.GetProperty("xTo").GetDouble()), Is.EqualTo(card.Plots.Select(p => p.XTo)));
      // The zoomed card's plots show its first screen: the run's first 2 s
      Assert.That(
        cards[1].GetProperty("plots").EnumerateArray().Select(p => p.GetProperty("xTo").GetDouble() - p.GetProperty("xFrom").GetDouble()),
        Is.All.EqualTo(2).Within(1e-9)
      );

      // Every frame of the run, as the analysis has it
      var frames = root.GetProperty("frames");
      var origin = run.Frames[0].FirstSeenTime;
      Assert.That(Column(frames, "t"), Is.EqualTo(run.Frames.Select(f => (long?)(f.FirstSeenTime - origin).Ticks)));
      Assert.That(Column(frames, "index"), Is.EqualTo(run.Frames.Select(f => (long?)f.FrameIndex)));
      Assert.That(Column(frames, "capture"), Is.EqualTo(run.Frames.Select(f => (long?)f.FirstCaptureIndex)));
      Assert.That(Column(frames, "error"), Is.EqualTo(run.Frames.Select(f => f.AnimationError?.Ticks)));
      Assert.That(Column(frames, "step"), Is.EqualTo(run.Frames.Skip(1).Select(f => f.DisplayDelta?.Ticks).Append(null)));
      Assert.That(Column(frames, "flags").Count(f => (f!.Value & (long)PresentedFrameFlags.Late) != 0), Is.EqualTo(run.Pacing!.LateFrames));

      // A section's report: a folder of its own (with its own copy, answered in advance) and only the section's frames
      var section = await PlaybackExport.WriteAsync(
        capture,
        runs,
        options with
        {
          FromSeconds = 2,
          ToSeconds = 4,
          TranscodeChoice = PlaybackTranscodeChoice.Yes,
        },
        _ => throw new AssertionException("answered in advance: no question")
      );
      Assert.That(section.Pages, Is.EqualTo(new[] { Path.Combine(report.OutputDirectory, "playback", "run-1-2s-4s", "index.html") }));
      Assert.That(section.Video.ExistsIn(Path.GetDirectoryName(section.Pages[0])!), Is.True);
      using var sectionData = PageData(File.ReadAllText(section.Pages[0]));
      var times = Column(sectionData.RootElement.GetProperty("frames"), "t");
      Assert.That(times, Is.Not.Empty);
      Assert.That(times, Is.All.InRange(2 * TimeSpan.TicksPerSecond, 4 * TimeSpan.TicksPerSecond));

      // The copy shows every capture at the time the import read from the recording
      var again = CaptureAnalyzer.Analyze(
        ImportFile(result.Video.PathIn(Path.GetDirectoryName(result.Pages[0])!)!, Path.Combine(m_directory, "copy")),
        new AnalysisOptions()
      );
      Assert.That(
        again.Capture.Rows.Select(r => (r.CaptureIndex, r.CaptureTime)),
        Is.EqualTo(report.Capture.Rows.Select(r => (r.CaptureIndex, r.CaptureTime))),
        "every frame of the copy at its timestamp"
      );
    }

    private static JsonDocument PageData(string html)
    {
      var match = Regex.Match(html, $"<script id=\"{PlaybackPage.DataElementId}\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
      Assert.That(match.Success, Is.True, "the page's data");
      return JsonDocument.Parse(match.Groups[1].Value);
    }

    private static long?[] Column(JsonElement frames, string name) =>
      frames.GetProperty(name).EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? (long?)null : v.GetInt64()).ToArray();

    private string ImportFile(string video, string output)
    {
      var media = MediaInput.Create(video, new MediaInputOptions(), output);
      using (var source = FfmpegCaptureSource.Start(media.ToCaptureOptions(m_ffmpeg), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output }, null, System.Threading.CancellationToken.None);
      return output;
    }
  }
}
