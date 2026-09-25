//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'camera-rig calibrate' and 'camera-rig verify' (VERY EXPERIMENTAL camera support): calibrate a high speed camera mounted in front of the
//* screen from a clip or a live device, and check it has not moved. Also the '--camera' step 'capture' and 'import' run: verify, then store
//* only the rectified marker zones.
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
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Ffmpeg;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class CameraRigCommand
  {
    public const string Experimental = "VERY EXPERIMENTAL";

    public static Command Create()
    {
      var command = new Command(
        "camera-rig",
        $"({Experimental}) Calibrate, verify and keep high speed cameras mounted in front of the screen, for 'capture --camera' and 'import --camera'."
      )
      {
        CreateCalibrate(),
        CreateVerify(),
        CreateList(),
        CreateDelete(),
      };
      return command;
    }

    /// <summary>--camera for 'capture' and 'import'.</summary>
    public static Option<string?> CameraOption() =>
      new Option<string?>("--camera")
      {
        Description =
          $"({Experimental}) A saved camera name or a rig file ('camera-rig calibrate'): verify the camera did not move, then store only the "
          + "rectified marker zones.",
      };

    /// <summary>--recorded-fps for video files.</summary>
    public static Option<double?> RecordedFpsOption() =>
      new Option<double?>("--recorded-fps")
      {
        Description =
          "Video files: the rate the clip was really recorded at (high speed camera clips are often stored at a slower playback rate). "
          + "Frame n is timed at n / rate; the file's timestamps are ignored.",
      };

    public static void PrintExperimentalWarning() => AnsiConsole.MarkupLineInterpolated($"[yellow]WARNING:[/] {CameraRig.ExperimentalNotice}");

    /// <summary>--camera: load the rig, verify the camera still sees the markers where it was calibrated, and rectify the zones.</summary>
    public static FfmpegCaptureOptions ApplyCamera(FfmpegCaptureOptions options, string nameOrPath, CancellationToken cancellationToken)
    {
      PrintExperimentalWarning();
      var rigPath = CameraRigLibrary.Resolve(nameOrPath);
      var rig = CameraRig.Load(rigPath);
      IReadOnlyList<CameraCheck> checks = Array.Empty<CameraCheck>();
      AnsiConsole
        .Status()
        .Spinner(Spinner.Known.Dots)
        .Start(
          $"Verifying the camera rig {Markup.Escape(Path.GetFileName(rigPath))}...",
          _ =>
          {
            using var source = FfmpegCaptureSource.Start(options with { Roi = null, Scale = null, Camera = null }, TimeSpan.FromSeconds(30));
            checks = CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), cancellationToken);
          }
        );
      PrintChecks(checks);
      if (checks.Any(c => c.Level == CameraCheckLevel.Fail))
        throw new InvalidOperationException("The camera rig check failed; recalibrate with 'camera-rig calibrate'.");
      return options with { Roi = null, Scale = null, Camera = rig };
    }

    public static void PrintChecks(IEnumerable<CameraCheck> checks)
    {
      foreach (var check in checks)
      {
        string color = check.Level switch
        {
          CameraCheckLevel.Pass => "green",
          CameraCheckLevel.Warn => "yellow",
          _ => "red",
        };
        AnsiConsole.MarkupLineInterpolated($"[{color}]{check.Level.ToString().ToUpperInvariant(), -4}[/] {check.Name}: {check.Message}");
      }
    }

    private static Command CreateCalibrate()
    {
      var source = new SourceOptions();
      var nameOption = new Option<string?>("--name")
      {
        Description = "Save the calibrated camera under this name in the camera library, to reuse it with --camera <name>.",
      };
      var outputOption = new Option<string?>("--output", "-o")
      {
        Description = $"Also (or instead) write the rig to this file (conventionally <name>{CameraRig.FileExtension}).",
      };
      var secondsOption = new Option<double>("--seconds")
      {
        Description = "Seconds of camera frames to calibrate from.",
        DefaultValueFactory = _ => 2,
      };
      var forceOption = new Option<bool>("--force") { Description = "Write the rig file even when a check failed." };
      var command = new Command(
        "calibrate",
        $"({Experimental}) Find both markers (TopLeft and BottomLeft slots) in a clip or a live camera, measure the scanout and check the setup."
      )
      {
        nameOption,
        outputOption,
        secondsOption,
        forceOption,
      };
      source.AddTo(command);
      command.SetAction(
        async (parseResult, cancellationToken) =>
        {
          try
          {
            PrintExperimentalWarning();
            var name = parseResult.GetValue(nameOption);
            var outputText = parseResult.GetValue(outputOption);
            if (name == null && outputText == null)
              throw new ArgumentException("Give the camera a name (--name, saved in the camera library) or a file (--output)");
            if (name != null && !CameraRigLibrary.IsValidName(name))
              throw new ArgumentException($"'{name}' is not a valid camera name: use letters, digits, '-', '_', '.' and spaces");
            using var work = new WorkDirectory();
            var options = source.Create(parseResult, work.Path);
            var calibratorOptions = new CameraCalibratorOptions
            {
              Seconds = parseResult.GetValue(secondsOption),
              Mode = source.ModeText(parseResult),
              InputFormat = options.InputFormat,
            };
            CameraRig? rig = null;
            await Task.Run(
              () =>
                AnsiConsole
                  .Status()
                  .Spinner(Spinner.Known.Dots)
                  .Start(
                    $"Calibrating from {Markup.Escape(options.Device.Name)}...",
                    _ =>
                    {
                      using var capture = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30));
                      rig = CameraCalibrator.Calibrate(capture, calibratorOptions, cancellationToken) with { Source = options.Device.Name };
                    }
                  ),
              CancellationToken.None
            );

            PrintChecks(rig!.Checks);
            if (rig.HasFailures && !parseResult.GetValue(forceOption))
            {
              AnsiConsole.MarkupLine("[red]Calibration failed; no rig file written.[/] Fix the setup and calibrate again (or pass --force).");
              return Program.ResultError;
            }
            if (name != null)
            {
              var saved = CameraRigLibrary.Save(rig, name);
              AnsiConsole.MarkupLineInterpolated($"Camera saved as [bold]{name}[/] ({saved}). Use it with '--camera \"{name}\"'.");
            }
            if (outputText != null)
            {
              var output = Path.GetFullPath(outputText);
              (name != null ? rig with { Name = name } : rig).Save(output);
              AnsiConsole.MarkupLineInterpolated($"Camera rig written to [bold]{output}[/]. Use it with 'capture --camera' or 'import --camera'.");
            }
            return Program.ResultSuccess;
          }
          catch (Exception ex)
          {
            Program.ReportError(ex);
            return Program.ResultError;
          }
        }
      );
      return command;
    }

    private static Command CreateVerify()
    {
      var source = new SourceOptions();
      var rigOption = new Option<string>("--rig") { Description = "The saved camera name or rig file to check against.", Required = true };
      var command = new Command("verify", $"({Experimental}) Check that the camera still sees both markers where the rig was calibrated.")
      {
        rigOption,
      };
      source.AddTo(command);
      command.SetAction(
        async (parseResult, cancellationToken) =>
        {
          try
          {
            PrintExperimentalWarning();
            var rig = CameraRigLibrary.Load(parseResult.GetValue(rigOption)!);
            using var work = new WorkDirectory();
            var options = source.Create(parseResult, work.Path);
            IReadOnlyList<CameraCheck> checks = Array.Empty<CameraCheck>();
            await Task.Run(
              () =>
              {
                using var capture = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30));
                checks = CameraCalibrator.Verify(rig, capture, new CameraCalibratorOptions(), cancellationToken);
              },
              CancellationToken.None
            );
            PrintChecks(checks);
            return checks.Any(c => c.Level == CameraCheckLevel.Fail) ? Program.ResultError : Program.ResultSuccess;
          }
          catch (Exception ex)
          {
            Program.ReportError(ex);
            return Program.ResultError;
          }
        }
      );
      return command;
    }

    private static Command CreateList()
    {
      var command = new Command("list", $"({Experimental}) The saved cameras in the camera library.");
      command.SetAction(_ =>
      {
        var saved = CameraRigLibrary.List();
        AnsiConsole.MarkupLineInterpolated($"[grey]Camera library: {CameraRigLibrary.DefaultDirectory}[/]");
        if (saved.Count == 0)
        {
          AnsiConsole.MarkupLine("No cameras saved yet. Calibrate one with 'camera-rig calibrate --name <name> ...'.");
          return Program.ResultSuccess;
        }
        var table = new Table().AddColumns("Name", "Camera", "Zones", "Scanout", "Calibrated", "Checks");
        foreach (var entry in saved)
        {
          if (entry.Rig is not { } rig)
          {
            table.AddRow(Markup.Escape(entry.Name), "[red]unreadable[/]", "", "", "", Markup.Escape(entry.Error ?? string.Empty));
            continue;
          }
          int warnings = rig.Checks.Count(c => c.Level != CameraCheckLevel.Pass);
          table.AddRow(
            Markup.Escape(entry.Name),
            FormattableString.Invariant($"{rig.CameraWidth}x{rig.CameraHeight} @ {rig.CameraFps:0.#} fps"),
            rig.Zones.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rig.ScanoutDelayMs is { } delay ? FormattableString.Invariant($"{delay:0.00} ms") : "-",
            rig.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            warnings == 0 ? "[green]all passed[/]" : $"[yellow]{warnings} warning(s)[/]"
          );
        }
        AnsiConsole.Write(table);
        return Program.ResultSuccess;
      });
      return command;
    }

    private static Command CreateDelete()
    {
      var nameArgument = new Argument<string>("name") { Description = "The saved camera to delete." };
      var command = new Command("delete", $"({Experimental}) Delete a saved camera from the camera library.") { nameArgument };
      command.SetAction(parseResult =>
      {
        try
        {
          var name = parseResult.GetValue(nameArgument)!;
          var backup = CameraRigLibrary.Delete(name);
          AnsiConsole.MarkupLineInterpolated($"Deleted the saved camera [bold]{name}[/]. A copy is kept in {backup}.");
          return Program.ResultSuccess;
        }
        catch (Exception ex)
        {
          Program.ReportError(ex);
          return Program.ResultError;
        }
      });
      return command;
    }

    /// <summary>A clip / image folder / stream argument, or a live device with its mode.</summary>
    private sealed class SourceOptions
    {
      private readonly Argument<string?> m_input = new Argument<string?>("input")
      {
        Description = "A camera clip (video file), a folder of images or a stream URL. Omit it to use a live device (--device).",
        Arity = ArgumentArity.ZeroOrOne,
      };
      private readonly Option<string?> m_device = new Option<string?>("--device", "-d")
      {
        Description = "A live camera (see 'devices'), or 'lavfi:<graph>' for an ffmpeg test source.",
      };
      private readonly Option<string?> m_mode = new Option<string?>("--mode")
      {
        Description = "Device mode: WIDTHxHEIGHT@FPS, WIDTHxHEIGHT or @FPS.",
      };
      private readonly Option<string?> m_inputFormat = new Option<string?>("--input-format")
      {
        Description = "Device pixel format or codec: mjpeg, yuyv422, nv12, ... (high frame rate UVC modes are usually mjpeg).",
      };
      private readonly Option<double?> m_fps = new Option<double?>("--fps")
      {
        Description = "Image sequences: the frame rate the images were captured at.",
      };
      private readonly Option<double?> m_recordedFps = RecordedFpsOption();
      private readonly Option<string?> m_ffmpeg = CommonOptions.Ffmpeg();

      public string? ModeText(ParseResult parseResult) => parseResult.GetValue(m_mode);

      public void AddTo(Command command)
      {
        command.Arguments.Add(m_input);
        command.Options.Add(m_device);
        command.Options.Add(m_mode);
        command.Options.Add(m_inputFormat);
        command.Options.Add(m_fps);
        command.Options.Add(m_recordedFps);
        command.Options.Add(m_ffmpeg);
      }

      public FfmpegCaptureOptions Create(ParseResult parseResult, string workDirectory)
      {
        var config = CommonOptions.LoadConfig(parseResult);
        var ffmpeg = FfmpegLocator.Find(parseResult.GetValue(m_ffmpeg), config);
        var input = parseResult.GetValue(m_input);
        var device = parseResult.GetValue(m_device);
        if ((input == null) == (device == null))
          throw new ArgumentException("Give either a clip (input) or a live device (--device), not both");
        if (input != null)
        {
          var media = MediaInput.Create(
            input,
            new MediaInputOptions { Fps = parseResult.GetValue(m_fps), RecordedFps = parseResult.GetValue(m_recordedFps) },
            workDirectory
          );
          return media.ToCaptureOptions(ffmpeg);
        }
        var modeText = parseResult.GetValue(m_mode);
        return new FfmpegCaptureOptions
        {
          FfmpegPath = ffmpeg,
          Device = CaptureDevice.Parse(device!),
          Mode = modeText != null ? RequestedMode.Parse(modeText) : default,
          InputFormat = parseResult.GetValue(m_inputFormat),
        };
      }
    }

    /// <summary>A temporary folder for an image sequence's ffconcat list.</summary>
    private sealed class WorkDirectory : IDisposable
    {
      public WorkDirectory()
      {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mb-framepacing", "camera-rig-" + Guid.NewGuid().ToString("N"));
      }

      public string Path { get; }

      public void Dispose()
      {
        try
        {
          if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      }
    }
  }
}
