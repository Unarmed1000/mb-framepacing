//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The playback page on the command line (render, analyze and import --playback): the options, the questions asked on the console (or not
//* asked: an answer given in advance by an option or the configuration, or input that cannot be read), and the progress. The work itself
//* is PlaybackExport's, shared with the GUI.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Charts;
using MB.FramePacing.Charts.Playback;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal sealed class PlaybackOutput
  {
    public Option<bool> Playback { get; } =
      new Option<bool>("--playback")
      {
        Description =
          "Also write a playback report (analysis/playback/run-<id>/index.html, a folder of its own): the report next to the recording it "
          + "was imported from, with a player and a playhead on the report. Recordings imported as a video file only.",
      };

    public Option<string?> Video { get; } =
      new Option<string?>("--video")
      {
        Description = "With --playback: the recording, instead of the one capture.json names (needed for imports made before it named one).",
      };

    public Option<PlaybackVideoChoice?> VideoChoice { get; } =
      new Option<PlaybackVideoChoice?>("--playback-video")
      {
        Description =
          "With --playback, a recording browsers can play: copy it into the report's folder (which then plays anywhere), link it, or ask "
          + "(default: the configuration's playbackVideo, else ask).",
        HelpName = "ask|copy|link",
      };

    public Option<PlaybackTranscodeChoice?> TranscodeChoice { get; } =
      new Option<PlaybackTranscodeChoice?>("--playback-transcode")
      {
        Description =
          "With --playback, a recording browsers cannot play: make a playable copy with ffmpeg (yes), link it as it is (no), or ask "
          + "(default: the configuration's playbackTranscode, else ask).",
        HelpName = "ask|yes|no",
      };

    /// <summary>Add the options to <paramref name="command"/>: the others only with --playback.</summary>
    public void AddTo(Command command)
    {
      foreach (var option in new Option[] { Playback, Video, VideoChoice, TranscodeChoice })
        command.Options.Add(option);
      command.Validators.Add(result =>
      {
        if (result.GetValue(Playback))
          return;
        foreach (var option in new Option[] { Video, VideoChoice, TranscodeChoice })
        {
          if (result.GetResult(option) is { Implicit: false })
            result.AddError($"{option.Name} goes with --playback.");
        }
      });
    }

    /// <summary>The pages of every run of a live analysis.</summary>
    public static void Write(
      ParseResult parseResult,
      PlaybackOutput output,
      AnalysisReport report,
      string ffmpeg,
      CancellationToken cancellationToken = default
    )
    {
      var captures = ChartRun.CapturesOf(report);
      var runs = report
        .Timeline.Runs.Select(run => new AnalysisOutputRun(ChartRun.From(report, run, captures), PlaybackExport.PrefixOf(report, run)))
        .ToList();
      Write(parseResult, output, PlaybackCapture.From(report), runs, ffmpeg, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Write the pages of <paramref name="runs"/> of <paramref name="capture"/> as the parsed options say, asking on the console when an
    /// answer is needed. Prints the pages.
    /// </summary>
    public static void Write(
      ParseResult parseResult,
      PlaybackOutput output,
      PlaybackCapture capture,
      IReadOnlyList<AnalysisOutputRun> runs,
      string ffmpeg,
      double? fromSeconds = null,
      double? toSeconds = null,
      ReportOptions? report = null,
      CancellationToken cancellationToken = default
    )
    {
      var config = CommonOptions.LoadConfig(parseResult);
      var (video, transcode) = PlaybackExportOptions.Choices(
        parseResult.GetValue(output.VideoChoice),
        parseResult.GetValue(output.TranscodeChoice),
        config
      );
      var options = new PlaybackExportOptions
      {
        FfmpegPath = ffmpeg,
        VideoPath = parseResult.GetValue(output.Video) is { } path ? Path.GetFullPath(path) : null,
        VideoChoice = video,
        TranscodeChoice = transcode,
        FromSeconds = fromSeconds,
        ToSeconds = toSeconds,
        Report = report ?? ReportOptions.Default,
        ToolVersion = Program.VersionString,
      };
      var progress = new ConsoleProgress();
      var result = PlaybackExport.WriteAsync(capture, runs, options, Ask, progress, cancellationToken).GetAwaiter().GetResult();
      progress.Finish();
      string kind = result.Video.Kind switch
      {
        PlaybackVideoKind.Copied => "a copy of the recording in its folder",
        PlaybackVideoKind.Transcoded => "a playable copy in its folder",
        _ => result.Video.Playable ? "the recording, linked" : "the recording, linked (browsers may not play it)",
      };
      AnsiConsole.MarkupLineInterpolated($"Playback report{(result.Pages.Count == 1 ? string.Empty : "s")}, playing {kind}:");
      foreach (var page in result.Pages)
        AnsiConsole.MarkupLineInterpolated($"  [link]{page}[/]");
    }

    /// <summary>A question on the console; with input that cannot be read, the answer that writes no video, and how to answer in advance.</summary>
    private static Task<bool> Ask(PlaybackQuestion question)
    {
      AnsiConsole.MarkupLineInterpolated($"[bold]{question.Title}[/]");
      AnsiConsole.MarkupLineInterpolated($"{question.Text}");
      if (Console.IsInputRedirected)
      {
        AnsiConsole.MarkupLineInterpolated(
          $"[yellow]No answer can be read (the input is redirected): {question.No.ToLowerInvariant()}.[/] Answer in advance with {question.Option}, or with {question.Setting} in the configuration ('config')."
        );
        return Task.FromResult(false);
      }
      bool yes =
        question.Kind == PlaybackQuestionKind.CopyOrLink
          ? AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]copy[/] into the folder, or [bold]link[/] the recording?")
              .AddChoice("copy")
              .AddChoice("link")
              .DefaultValue("link")
              .ShowChoices()
          ) == "copy"
          : AnsiConsole.Confirm("Make a playable copy?", defaultValue: false);
      AnsiConsole.MarkupLineInterpolated(
        $"[grey]Answer it for good with {question.Setting} in the configuration ('config'), or per run with {question.Option}.[/]"
      );
      return Task.FromResult(yes);
    }

    /// <summary>The export's steps and how far a copy is, on one console line that updates.</summary>
    private sealed class ConsoleProgress : IProgress<PlaybackProgress>
    {
      private string? m_step;
      private int m_percent = -1;
      private bool m_open;

      public void Report(PlaybackProgress value)
      {
        lock (this)
        {
          int percent = value.Fraction is { } fraction ? (int)Math.Floor(fraction * 100) : -1;
          if (value.Step == m_step && percent == m_percent)
            return;
          bool sameStep = value.Step == m_step;
          m_step = value.Step;
          m_percent = percent;
          string text = percent >= 0 ? $"{value.Step}... {percent} %" : $"{value.Step}...";
          if (Console.IsOutputRedirected)
          {
            if (!sameStep || percent % 25 == 0)
              Console.WriteLine(text);
            return;
          }
          if (m_open && !sameStep)
            Console.WriteLine();
          Console.Write("\r" + text.PadRight(Math.Max(text.Length, 40)));
          m_open = true;
        }
      }

      public void Finish()
      {
        lock (this)
        {
          if (m_open)
            Console.WriteLine();
          m_open = false;
        }
      }
    }

    /// <summary>The ffmpeg the playback page uses: --ffmpeg, else as the configuration finds it.</summary>
    public static string FindFfmpeg(ParseResult parseResult, Option<string?> ffmpegOption) =>
      FfmpegLocator.Find(parseResult.GetValue(ffmpegOption), CommonOptions.LoadConfig(parseResult));
  }
}
