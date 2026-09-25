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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;
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
      var cameraOption = new Option<bool>("--camera")
      {
        Description =
          $"({CameraRigCommand.Experimental}) Film the synthetic game with a simulated high speed camera (perspective, rolling scanout, panel "
          + "response, blur, noise) and run the camera pipeline: rig calibration, rectified zones, camera analysis. Try --fps 1000 --refresh 60.",
      };
      var tearOption = new Option<int>("--tear-every")
      {
        Description = "--camera: every n-th frame is presented mid-scanout (vsync off; 0 = never).",
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
        cameraOption,
        tearOption,
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
                TearEvery = parseResult.GetValue(tearOption),
                RunName = "selftest",
                RunId = 1,
              }
            );
            if (parseResult.GetValue(cameraOption))
              return await RunCameraAsync(scenario, directory, cancellationToken);
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

    /// <summary>
    /// EXPERIMENTAL: the camera pipeline on the synthetic camera. Checks every presented frame is found, display deltas are within two camera
    /// periods of the truth, the scanout delay is right and tears (--tear-every) are found; prints how long each stage took.
    /// </summary>
    private static async Task<int> RunCameraAsync(SyntheticScenario scenario, string directory, CancellationToken cancellationToken)
    {
      CameraRigCommand.PrintExperimentalWarning();
      var camera = new SyntheticCamera(scenario, new SyntheticCameraOptions());
      AnsiConsole.MarkupLineInterpolated(
        $"Synthetic {scenario.Options.RefreshHz:0.##} Hz game filmed by a simulated {scenario.Options.CaptureFps:0.##} fps camera ({camera.Options.CameraWidth}x{camera.Options.CameraHeight}), {scenario.Options.TotalSeconds:0.##} s -> {directory}"
      );

      var clock = Stopwatch.StartNew();
      var frames = CameraFrameSet.Collect(new SyntheticCameraSource(camera), 0.5, 1L << 30, TimeSpan.FromMinutes(5), cancellationToken);
      var collectTime = clock.Elapsed;
      clock.Restart();
      var rig = CameraCalibrator.Calibrate(frames, new CameraCalibratorOptions(), cancellationToken);
      var calibrateTime = clock.Elapsed;
      CameraRigCommand.PrintChecks(rig.Checks);
      if (rig.HasFailures)
      {
        AnsiConsole.MarkupLine("[red]FAIL[/]: the camera rig calibration failed");
        return Program.ResultError;
      }

      clock.Restart();
      CaptureResult capture;
      using (var source = new RectifyingCaptureSource(new SyntheticCameraSource(camera), rig))
      {
        var runOptions = new CaptureRunOptions
        {
          OutputDirectory = directory,
          ToolVersion = Program.VersionString,
          Camera = rig,
        };
        capture = await Task.Run(() => CaptureCommand.RunWithStatus(source, runOptions, cancellationToken), CancellationToken.None);
      }
      var captureTime = clock.Elapsed;
      CaptureCommand.PrintResult(capture.Session);

      clock.Restart();
      var report = CaptureAnalyzer.Analyze(directory, new AnalysisOptions { ToolVersion = Program.VersionString });
      var analyzeTime = clock.Elapsed;
      AnalyzeCommand.Print(report);

      AnsiConsole.MarkupLineInterpolated(
        $"[grey]Timings: rendering {frames.Count} calibration frames {collectTime.TotalSeconds:0.0} s, calibration {calibrateTime.TotalSeconds:0.00} s, rendering + rectifying + recording {capture.Session.FramesWritten} frames {captureTime.TotalSeconds:0.0} s, analysis {analyzeTime.TotalSeconds:0.00} s ({capture.Session.FramesWritten / Math.Max(0.001, analyzeTime.TotalSeconds):0} captures/s).[/]"
      );
      return VerifyCamera(camera, report) ? Program.ResultSuccess : Program.ResultError;
    }

    private static bool VerifyCamera(SyntheticCamera camera, AnalysisReport report)
    {
      var failures = new List<string>();
      var scenario = camera.Scenario;
      var truth = scenario.PresentedFrames.Where(f => f.Payload.Kind == MarkerKind.Frame && f.Payload.RunId == scenario.Options.RunId).ToList();
      var tears = truth.Where(f => f.DisplayTicks % scenario.RefreshIntervalTicks != 0).Select(f => f.Payload.FrameIndex).ToHashSet();
      var run = report.Timeline.Runs.FirstOrDefault();
      double period = TimeSpan.TicksPerSecond / scenario.Options.CaptureFps;
      int checkedFrames = 0;
      int insideTears = 0;
      if (run == null)
        failures.Add("no run was found");
      else
      {
        // Frames only seen below a tear never reach the timing zone; every other frame must be found
        var found = run.Frames.Select(f => f.FrameIndex).ToHashSet();
        var expected = truth.Where(f => !tears.Contains(f.Payload.FrameIndex) || found.Contains(f.Payload.FrameIndex)).ToList();
        if (!run.Frames.Select(f => f.FrameIndex).SequenceEqual(expected.Select(f => f.Payload.FrameIndex)))
          failures.Add($"found {run.Frames.Count} presented frames, expected {expected.Count}");
        else
        {
          for (int i = 1; i < expected.Count; ++i)
          {
            if (tears.Contains(expected[i].Payload.FrameIndex) || tears.Contains(expected[i - 1].Payload.FrameIndex))
              continue;
            double truthDelta = camera.ToCameraTicks(expected[i].DisplayTicks - expected[i - 1].DisplayTicks);
            ++checkedFrames;
            if (Math.Abs(run.Frames[i].DisplayDeltaTicks!.Value - truthDelta) > (2 * period) + 1)
              failures.Add($"frame {expected[i].Payload.FrameIndex}: display delta off by more than two camera periods");
          }
        }
        double scanout = camera.ToCameraTicks(camera.ZoneScanTicks(1) - camera.ZoneScanTicks(0)) / TimeSpan.TicksPerMillisecond;
        if (run.Camera == null || Math.Abs(run.Camera.ScanoutDelay.P50 - scanout) > Math.Max(1, period / TimeSpan.TicksPerMillisecond))
          failures.Add($"scanout delay {run.Camera?.ScanoutDelay.P50:0.00} ms, expected {scanout:0.00} ms");
        // A tear right at the start or end of the run can not be told from the start/end marker transition
        if (run.Frames.Count > 0)
        {
          ulong firstFound = run.Frames[0].FrameIndex;
          ulong lastFound = run.Frames[^1].FrameIndex;
          insideTears = tears.Count(t => t > firstFound && t < lastFound);
          long tearsFound = run.Camera?.TornFrames + run.Camera?.SecondZoneOnlyFrames ?? 0;
          if (tearsFound != insideTears)
            failures.Add($"{tearsFound} tears found, expected {insideTears} ({string.Join(", ", tears.Order())})");
        }
      }

      AnsiConsole.WriteLine();
      if (failures.Count == 0)
      {
        AnsiConsole.MarkupLineInterpolated(
          $"[green]PASS[/] (camera, {CameraRigCommand.Experimental}): all presented frames found, {checkedFrames} display deltas within two camera periods, scanout delay as simulated, all {insideTears} tears inside the run found."
        );
        return true;
      }
      foreach (var failure in failures.Take(20))
        AnsiConsole.MarkupLineInterpolated($"[red]FAIL[/]: {failure}");
      return false;
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
