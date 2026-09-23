//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'selftest': run the whole pipeline without hardware. A synthetic game (with stalls and skipped frames) is captured through the real
//* recorder into a real capture file and analysed; the result is compared with the synthetic ground truth.
//*
//* (c) 2026 Mana Battery
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
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class SelfTestCommand
  {
    public static Command Create()
    {
      var fpsOption = new Option<double>("--fps") { Description = "Capture rate.", DefaultValueFactory = _ => 500 };
      var refreshOption = new Option<double>("--refresh") { Description = "Simulated display refresh rate (Hz).", DefaultValueFactory = _ => 144 };
      var secondsOption = new Option<double>("--seconds") { Description = "Length of the measured run.", DefaultValueFactory = _ => 5 };
      var sizeOption = new Option<string>("--size") { Description = "Stored frame size.", DefaultValueFactory = _ => "960x540" };
      var stallOption = new Option<int>("--stall-every")
      {
        Description = "Every n-th frame misses a vsync (0 = never).",
        DefaultValueFactory = _ => 37,
      };
      var skipOption = new Option<int>("--skip-every")
      {
        Description = "Every n-th frame is never presented (0 = never).",
        DefaultValueFactory = _ => 53,
      };
      var unpacedOption = new Option<bool>("--unpaced") { Description = "Produce frames as fast as possible instead of in real time." };
      var outputOption = new Option<string?>("--output", "-o")
      {
        Description = "Keep the capture in this directory (default: a temporary directory that is deleted).",
      };

      var command = new Command("selftest", "Capture and analyse a synthetic game to verify the whole pipeline on this machine.")
      {
        fpsOption,
        refreshOption,
        secondsOption,
        sizeOption,
        stallOption,
        skipOption,
        unpacedOption,
        outputOption,
      };
      command.SetAction(
        async (parseResult, cancellationToken) =>
        {
          var keep = parseResult.GetValue(outputOption);
          var directory =
            keep != null ? Path.GetFullPath(keep) : Path.Combine(Path.GetTempPath(), "mb-framepacing-selftest-" + Guid.NewGuid().ToString("N"));
          try
          {
            var (width, height) = MB.FramePacing.Capture.Ffmpeg.RequestedMode.ParseSize(parseResult.GetValue(sizeOption)!, "--size");
            var scenario = new SyntheticScenario(
              new SyntheticScenarioOptions
              {
                CaptureFps = parseResult.GetValue(fpsOption),
                RefreshHz = parseResult.GetValue(refreshOption),
                RunSeconds = parseResult.GetValue(secondsOption),
                Width = width,
                Height = height,
                ModuleSizePx = 3,
                OriginX = 32,
                OriginY = 32,
                StallEvery = parseResult.GetValue(stallOption),
                SkipEvery = parseResult.GetValue(skipOption),
                RunName = "selftest",
                RunId = 1,
              }
            );
            bool paced = !parseResult.GetValue(unpacedOption);
            AnsiConsole.MarkupLineInterpolated(
              $"Synthetic {scenario.Options.RefreshHz:0.##} Hz game, captured at {scenario.Options.CaptureFps:0.##} fps, {width}x{height}, {scenario.Options.TotalSeconds:0.##} s {(paced ? "in real time" : "unpaced")} -> {directory}"
            );

            CaptureResult capture;
            using (var source = new SyntheticCaptureSource(scenario, paced))
            {
              var runOptions = new CaptureRunOptions { OutputDirectory = directory, ToolVersion = Program.VersionString };
              capture = await Task.Run(() => CaptureCommand.RunWithStatus(source, runOptions, cancellationToken), CancellationToken.None);
            }
            CaptureCommand.PrintResult(capture.Session);

            var report = CaptureAnalyzer.Analyze(directory, new AnalysisOptions { ToolVersion = Program.VersionString });
            AnalyzeCommand.Print(report);
            return Verify(scenario, capture.Session, report) ? Program.ResultSuccess : Program.ResultError;
          }
          catch (Exception ex)
          {
            Program.ReportError(ex);
            return Program.ResultError;
          }
          finally
          {
            if (keep == null && Directory.Exists(directory))
              Directory.Delete(directory, recursive: true);
          }
        }
      );
      return command;
    }

    private static bool Verify(SyntheticScenario scenario, CaptureSessionInfo session, AnalysisReport report)
    {
      var failures = new List<string>();
      if (session.FramesDroppedByRecorder > 0)
        failures.Add(
          $"the recorder dropped {session.FramesDroppedByRecorder} frames: this machine/disk can not sustain {scenario.Options.CaptureFps:0} fps at this size"
        );

      // Ground truth: the application frame visible at each capture, collapsed to presented frames
      var expected = new List<(ulong FrameIndex, long FirstSeen)>();
      for (long i = 0; i < scenario.CaptureCount; ++i)
      {
        int index = scenario.PresentedIndexAt(i);
        if (index < 0)
          continue;
        var payload = scenario.PresentedFrames[index].Payload;
        if (payload.Kind == MarkerKind.Frame && (expected.Count == 0 || expected[^1].FrameIndex != payload.FrameIndex))
          expected.Add((payload.FrameIndex, scenario.CaptureTicks(i)));
      }

      var run = report.Timeline.Runs.FirstOrDefault();
      if (run == null)
        failures.Add("no run was found");
      else if (session.FramesDroppedByRecorder == 0)
      {
        var actual = run.Frames.Select(f => (f.FrameIndex, f.FirstSeenTicks)).ToList();
        int mismatches = expected.Zip(actual).Count(pair => pair.First != pair.Second) + Math.Abs(expected.Count - actual.Count);
        if (mismatches > 0)
          failures.Add($"{mismatches} of {expected.Count} presented frames differ from the ground truth");
      }

      AnsiConsole.WriteLine();
      if (failures.Count == 0)
      {
        AnsiConsole.MarkupLineInterpolated($"[green]PASS[/]: all {expected.Count} presented frames match the ground truth, no frames dropped.");
        return true;
      }
      foreach (var failure in failures)
        AnsiConsole.MarkupLineInterpolated($"[red]FAIL[/]: {failure}");
      return false;
    }
  }
}
