//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Playback reports (PlaybackExport.WriteAsync) of the SDK's golden analysis, with ffmpeg's steps replaced: every report has a folder of its
//* own that names nothing outside it, and saving one never touches another; a recording browsers play is copied, one they cannot play is
//* made playable only after a yes (given in advance or to the question), else the report has no video; saving the same report again uses
//* its video again while the recording is unchanged; a capture the page cannot show is refused. Also the answer's precedence (an option over
//* the configuration over asking) and the license notice every page carries.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Charts.Playback;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PlaybackExportTests
  {
    private static readonly VideoCodecInfo g_playable = new VideoCodecInfo(
      "mp4",
      "mov,mp4,m4a,3gp,3g2,mj2",
      "h264",
      "High",
      "yuv420p",
      60,
      TimeSpan.FromSeconds(8)
    );

    private static readonly VideoCodecInfo g_unplayable = new VideoCodecInfo(
      "mp4",
      "mov,mp4,m4a,3gp,3g2,mj2",
      "h264",
      "High 4:4:4 Predictive",
      "yuv444p",
      60,
      TimeSpan.FromSeconds(8)
    );

    private IReadOnlyList<AnalysisOutputRun> m_runs = Array.Empty<AnalysisOutputRun>();
    private string m_directory = string.Empty;
    private string m_source = string.Empty;
    private PlaybackCapture m_capture = null!;
    private readonly List<PlaybackQuestion> m_asked = new List<PlaybackQuestion>();
    private readonly List<string> m_transcoded = new List<string>();

    [OneTimeSetUp]
    public void ReadTheGoldenAnalysis()
    {
      // The data modules' golden data: a test clip imported and analysed (sdk/test-data/data)
      string golden = Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "..", "..", "sdk", "test-data", "data", "60-busy-full-rate"));
      m_runs = AnalysisOutput.Read(golden);
    }

    [SetUp]
    public void SetUp()
    {
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path.Combine(m_directory, "capture", "analysis"));
      m_source = Path.Combine(m_directory, "recordings", "my capture.mp4");
      Directory.CreateDirectory(Path.GetDirectoryName(m_source)!);
      File.WriteAllText(m_source, "the recording");
      m_capture = new PlaybackCapture(Path.Combine(m_directory, "capture", "analysis"), m_source, "Device", null, false);
      m_asked.Clear();
      m_transcoded.Clear();
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        Directory.Delete(m_directory, recursive: true);
      }
      catch (IOException) { }
    }

    private Task<PlaybackResult> Save(
      VideoCodecInfo codec,
      bool answer,
      PlaybackTranscodeChoice transcode = PlaybackTranscodeChoice.Ask,
      double? fromSeconds = null,
      double? toSeconds = null,
      PlaybackCapture? capture = null,
      string? videoUrl = null,
      Func<string, VideoCodecInfo>? probe = null
    ) =>
      PlaybackExport.WriteAsync(
        capture ?? m_capture,
        m_runs,
        new PlaybackExportOptions
        {
          FfmpegPath = "ffmpeg",
          TranscodeChoice = transcode,
          VideoUrl = videoUrl,
          FromSeconds = fromSeconds,
          ToSeconds = toSeconds,
        },
        question =>
        {
          m_asked.Add(question);
          return Task.FromResult(answer);
        },
        null,
        CancellationToken.None,
        probe ?? (_ => codec),
        (_, target, _, _, _) =>
        {
          m_transcoded.Add(target);
          File.WriteAllText(target, "a playable copy");
        }
      );

    /// <summary>The folder of the whole run's report.</summary>
    private string WholeRun => Path.Combine(m_capture.PlaybackDirectory, "run-1");

    private static string[] Files(string folder) =>
      Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();

    /// <summary>No file of the report's folder names a local path: not the recording's folder, not the capture's (as text or as JSON).</summary>
    private void AssertNamesNoLocalPath(string folder)
    {
      foreach (
        string file in Directory
          .GetFiles(folder)
          .Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal))
      )
      {
        string text = File.ReadAllText(file);
        foreach (string local in new[] { m_directory, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) })
        {
          Assert.That(text, Does.Not.Contain(local), $"{Path.GetFileName(file)} names a local path");
          Assert.That(
            text,
            Does.Not.Contain(System.Text.Json.JsonSerializer.Serialize(local).Trim('"')),
            $"{Path.GetFileName(file)} names a local path"
          );
        }
      }
    }

    [Test]
    public async Task Playable_IsCopied_WithoutAQuestion_TheReportsFolderPlaysOnItsOwn()
    {
      var result = await Save(g_playable, answer: false);

      Assert.That(m_asked, Is.Empty, "a copy needs no question");
      Assert.That(result.Pages, Is.EqualTo(new[] { Path.Combine(WholeRun, "index.html") }));
      Assert.That(result.Video.Kind, Is.EqualTo(PlaybackVideoKind.Copied));
      Assert.That(result.Video.Playable, Is.True);
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }));
      Assert.That(File.ReadAllText(Path.Combine(WholeRun, "video.mp4")), Is.EqualTo("the recording"));
      Assert.That(PlaybackVideo.Read(WholeRun), Is.EqualTo(result.Video));
      Assert.That(result.Video.SourceName, Is.EqualTo("my capture.mp4"));
      Assert.That(File.ReadAllText(result.Pages[0]), Does.Contain("\"url\":\"video.mp4\""));
      Assert.That(m_transcoded, Is.Empty);
      AssertNamesNoLocalPath(WholeRun);
    }

    [Test]
    public async Task ThePage_CarriesTheLicensesTermsAndRequiredNotice_SoItCanBeSentOn()
    {
      var result = await Save(g_playable, answer: false);

      // The root LICENSE's own lines: the terms' URL and every "Required Notice:" line, each on a line of its own in the page
      string root = Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "..", ".."));
      var license = File.ReadAllLines(Path.Combine(root, "LICENSE"));
      var notices = license.Where(line => line.StartsWith("Required Notice:", StringComparison.Ordinal)).ToList();
      Assert.That(notices, Is.Not.Empty);
      var page = File.ReadAllLines(result.Pages[0]);
      Assert.That(page, Has.Some.EqualTo("PolyForm Perimeter License 1.0.1: https://polyformproject.org/licenses/perimeter/1.0.1"));
      Assert.That(license, Has.Some.EqualTo("<https://polyformproject.org/licenses/perimeter/1.0.1>"), "the LICENSE names the same terms");
      foreach (string notice in notices)
        Assert.That(page, Has.Some.EqualTo(notice));
    }

    [Test]
    public async Task Unplayable_AskedAndTranscoded_TheReportHasAPlayableCopy()
    {
      var result = await Save(g_unplayable, answer: true);

      Assert.That(m_asked, Has.Count.EqualTo(1));
      Assert.That(m_asked[0].Target, Is.EqualTo(Path.Combine(WholeRun, "video.mp4")));
      Assert.That(m_transcoded, Is.EqualTo(new[] { Path.Combine(WholeRun, "video.mp4") }));
      Assert.That(result.Video.Kind, Is.EqualTo(PlaybackVideoKind.Transcoded));
      Assert.That(result.Video.Playable, Is.True);
      Assert.That(result.Video.SourcePlayable, Is.False);
      AssertNamesNoLocalPath(WholeRun);
    }

    [Test]
    public async Task Unplayable_Declined_TheReportHasNoVideo_AndSaysHowToMakeOne()
    {
      var result = await Save(g_unplayable, answer: false);

      Assert.That(m_asked, Has.Count.EqualTo(1));
      Assert.That(m_transcoded, Is.Empty);
      Assert.That(result.Video.Kind, Is.EqualTo(PlaybackVideoKind.None));
      Assert.That(result.Video.Playable, Is.False);
      Assert.That(result.Video.Problem, Does.Contain("4:4:4"));
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json" }));
      string page = File.ReadAllText(result.Pages[0]);
      Assert.That(page, Does.Contain("\"url\":null"));
      Assert.That(page, Does.Contain("mb-framepacing render \\u003Ccapture folder\\u003E --playback --playback-transcode yes"));
      AssertNamesNoLocalPath(WholeRun);
    }

    [TestCase(PlaybackTranscodeChoice.Yes, PlaybackVideoKind.Transcoded)]
    [TestCase(PlaybackTranscodeChoice.No, PlaybackVideoKind.None)]
    public async Task Unplayable_AnsweredInAdvance_NeverAsks(PlaybackTranscodeChoice choice, PlaybackVideoKind kind)
    {
      Assert.That((await Save(g_unplayable, answer: false, transcode: choice)).Video.Kind, Is.EqualTo(kind));
      Assert.That(m_asked, Is.Empty);
    }

    [Test]
    public async Task EachReport_HasAFolderOfItsOwn_AndSavingOneNeverTouchesAnother()
    {
      var whole = await Save(g_unplayable, answer: false, transcode: PlaybackTranscodeChoice.Yes);
      var section = await Save(g_unplayable, answer: false, transcode: PlaybackTranscodeChoice.Yes, fromSeconds: 1, toSeconds: 3);

      string sectionFolder = Path.Combine(m_capture.PlaybackDirectory, "run-1-1s-3s");
      Assert.That(section.Pages, Is.EqualTo(new[] { Path.Combine(sectionFolder, "index.html") }));
      Assert.That(Files(sectionFolder), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }), "the section's own copy");
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }));

      // The whole run's report saved again with another answer: only its own folder changes
      string sectionPage = File.ReadAllText(section.Pages[0]);
      await Save(g_unplayable, answer: false, transcode: PlaybackTranscodeChoice.No);
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json" }), "the report replaced has no video now");
      Assert.That(File.ReadAllText(section.Pages[0]), Is.EqualTo(sectionPage));
      Assert.That(Files(sectionFolder), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }));
      Assert.That(PlaybackVideo.Read(sectionFolder), Is.EqualTo(section.Video));
      Assert.That(PlaybackVideo.Read(WholeRun)?.Kind, Is.Not.EqualTo(whole.Video.Kind));
    }

    [Test]
    public async Task SavingTheSameReportAgain_UsesItsVideo_WhileTheRecordingIsUnchanged()
    {
      var first = await Save(g_unplayable, answer: true);
      var again = await Save(g_unplayable, answer: false);

      Assert.That(m_asked, Has.Count.EqualTo(1), "nothing asked again");
      Assert.That(again.Video, Is.EqualTo(first.Video));
      Assert.That(m_transcoded, Has.Count.EqualTo(1));

      // The recording changed: a new question, and the report's new copy replaces its old one once the page plays it
      File.AppendAllText(m_source, " and more");
      var changed = await Save(g_unplayable, answer: true);
      Assert.That(m_asked, Has.Count.EqualTo(2));
      Assert.That(m_transcoded[^1], Is.EqualTo(Path.Combine(WholeRun, "video-2.mp4")), "written next to the video the page still plays");
      Assert.That(changed.Video.VideoFile, Is.EqualTo("video-2.mp4"));
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json", "video-2.mp4" }));
    }

    [Test]
    public async Task TheRecordingGone_TheReportsCopyStillPlays_ElseItIsAnError()
    {
      var copied = await Save(g_playable, answer: false);
      File.Delete(m_source);

      Assert.That((await Save(g_playable, answer: false)).Video, Is.EqualTo(copied.Video));
      File.Delete(Path.Combine(WholeRun, "video.mp4"));
      await Assert.ThrowsAsync<FileNotFoundException>(() => Save(g_playable, answer: false));
    }

    [Test]
    public async Task ACaptureThePageCannotShow_IsRefused_BeforeAnything()
    {
      var camera = m_capture with { Camera = true };
      var recorded = m_capture with { RecordedFps = 960 };
      var host = m_capture with { TimeSource = "Host" };
      var unnamed = m_capture with { InputPath = null };
      var images = m_capture with { InputPath = m_directory };

      foreach (var capture in new[] { camera, recorded, host, unnamed, images })
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save(g_playable, answer: true, capture: capture));
      Assert.That(m_asked, Is.Empty);
      Assert.That(Directory.Exists(m_capture.PlaybackDirectory), Is.False);
      Assert.That(unnamed.Problem(), Does.Contain("--video"));
      Assert.That(unnamed.Problem(m_source), Is.Null, "a video named for it");
      Assert.That(host.Problem(), Does.Contain("Host"));
    }

    [Test]
    public async Task ANamedVideo_IsWrittenIntoThePageAsGiven_NothingIsCopiedOrAsked()
    {
      const string Url = "../../videos/60-busy adaptive.mp4";
      var result = await Save(g_unplayable, answer: true, videoUrl: Url, probe: _ => throw new AssertionException("nothing to probe"));

      Assert.That(m_asked, Is.Empty);
      Assert.That(m_transcoded, Is.Empty);
      Assert.That(result.Video.Kind, Is.EqualTo(PlaybackVideoKind.External));
      Assert.That(result.Video.Url, Is.EqualTo(Url));
      Assert.That(result.Video.Playable, Is.True);
      Assert.That(result.Warnings, Is.Empty, "a file not made yet is not judged");
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json" }));
      Assert.That(File.ReadAllText(result.Pages[0]), Does.Contain($"\"url\":\"{Url}\""));
      Assert.That(PlaybackVideo.Read(WholeRun), Is.EqualTo(result.Video));

      // The recording is not needed: an import that names none, and a URL
      var unnamed = await Save(g_unplayable, answer: true, capture: m_capture with { InputPath = null }, videoUrl: "https://example.com/run.mp4");
      Assert.That(unnamed.Video.Url, Is.EqualTo("https://example.com/run.mp4"));
      Assert.That(unnamed.Video.SourceName, Is.EqualTo("run.mp4"));
      // Its times must still be a video's own
      await Assert.ThrowsAsync<InvalidOperationException>(() =>
        Save(g_playable, answer: true, capture: m_capture with { Camera = true }, videoUrl: Url)
      );
    }

    [Test]
    public async Task ANamedVideo_ReplacesTheReportsCopy_AndWithoutIt_TheCopyComesBack()
    {
      await Save(g_playable, answer: false);
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }));

      await Save(g_playable, answer: false, videoUrl: "../../videos/run.mp4");
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json" }), "the report replaced plays the named video");

      var again = await Save(g_playable, answer: false);
      Assert.That(again.Video.Kind, Is.EqualTo(PlaybackVideoKind.Copied));
      Assert.That(Files(WholeRun), Is.EqualTo(new[] { "index.html", "playback.json", "video.mp4" }));
    }

    [Test]
    public async Task ANamedVideo_ThatIsThereAndShorterThanTheRun_IsAWarning_NeverAnError()
    {
      // ../../videos from the report's folder (analysis/playback/run-1) is analysis/videos
      string video = Path.Combine(m_capture.AnalysisDirectory, "videos", "run.mp4");
      Directory.CreateDirectory(Path.GetDirectoryName(video)!);
      File.WriteAllText(video, "a web encode");
      var oneSecond = g_playable with { Duration = TimeSpan.FromSeconds(1) };

      var shortVideo = await Save(g_playable, answer: false, videoUrl: "../../videos/run.mp4", probe: _ => oneSecond);
      Assert.That(shortVideo.Warnings, Has.Count.EqualTo(1));
      Assert.That(shortVideo.Warnings[0], Does.Contain("1.0 s long"));

      var longVideo = await Save(
        g_playable,
        answer: false,
        videoUrl: "../../videos/run.mp4",
        probe: _ => g_playable with { Duration = TimeSpan.FromHours(1) }
      );
      Assert.That(longVideo.Warnings, Is.Empty);

      var unreadable = await Save(
        g_playable,
        answer: false,
        videoUrl: "../../videos/run.mp4",
        probe: _ => throw new InvalidDataException("no video")
      );
      Assert.That(unreadable.Warnings, Is.Empty, "a file ffmpeg cannot describe is not judged");
    }

    [Test]
    public void Choice_AnOptionWins_OverTheConfiguration_OverAsking()
    {
      var config = new FramePacingConfig { PlaybackTranscode = PlaybackTranscodeChoice.No };

      Assert.That(PlaybackExportOptions.Choice(null, new FramePacingConfig()), Is.EqualTo(PlaybackTranscodeChoice.Ask));
      Assert.That(PlaybackExportOptions.Choice(null, config), Is.EqualTo(PlaybackTranscodeChoice.No));
      Assert.That(PlaybackExportOptions.Choice(PlaybackTranscodeChoice.Yes, config), Is.EqualTo(PlaybackTranscodeChoice.Yes));
    }
  }
}
