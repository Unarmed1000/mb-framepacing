//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'import': record from something other than a capture card - a video file, a folder of images or a stream URL - into a capture folder that
//* 'analyze' reads like any other capture.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Marker;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class ImportCommand
  {
    public static Command Create()
    {
      var inputArgument = new Argument<string>("input")
      {
        Description = "A video file (mp4, mkv, mov, ...), a folder of images (png, jpg, bmp, ...) or a stream URL (rtsp://, srt://, udp://, ...).",
      };
      var fpsOption = new Option<double?>("--fps") { Description = "Image sequences: the frame rate the images were captured at." };
      var timestampsOption = new Option<string?>("--timestamps")
      {
        Description = "Image sequences: a CSV with 'fileName,timeMs' per image (overrides --fps; the images are used in this order).",
      };
      var scaleOption = new Option<string?>("--scale") { Description = "Stored frame size WIDTHxHEIGHT (area downscale). Prefer integer ratios." };
      var roiOption = new Option<string?>("--roi") { Description = "Only store this region: x,y,width,height (source pixels, before --scale)." };
      var durationOption = new Option<string?>("--duration", "-t") { Description = "Stop after this long (mostly for streams), e.g. 30s." };
      var waitOption = new Option<bool>("--wait-for-start") { Description = "Skip everything before the start marker (keeps a short pre-roll)." };
      var stopOption = new Option<bool>("--stop-at-end") { Description = "Stop once the end marker of the run has been seen." };
      var outputOption = new Option<string?>("--output", "-o")
      {
        Description = "Capture folder to create (default: a new import-<date>-<time> folder in the configured capture directory).",
      };
      var analyzeOption = new Option<bool>("--analyze") { Description = "Run 'analyze' on the result." };
      var ffmpegOption = CommonOptions.Ffmpeg();

      var command = new Command("import", "Read a video file, an image sequence or a stream instead of a capture card.")
      {
        inputArgument,
        fpsOption,
        timestampsOption,
        scaleOption,
        roiOption,
        durationOption,
        waitOption,
        stopOption,
        outputOption,
        analyzeOption,
        ffmpegOption,
      };

      command.SetAction(
        async (parseResult, cancellationToken) =>
        {
          try
          {
            var config = CommonOptions.LoadConfig(parseResult);
            var ffmpeg = FfmpegLocator.Find(parseResult.GetValue(ffmpegOption), config);
            var input = parseResult.GetValue(inputArgument)!;
            var output = Path.GetFullPath(parseResult.GetValue(outputOption) ?? DefaultOutputDirectory(config));
            var scaleText = parseResult.GetValue(scaleOption);
            var roiText = parseResult.GetValue(roiOption);

            var media = MediaInput.Create(
              input,
              new MediaInputOptions { Fps = parseResult.GetValue(fpsOption), TimestampFile = parseResult.GetValue(timestampsOption) },
              output
            );
            var options = media.ToCaptureOptions(ffmpeg) with
            {
              Scale = scaleText != null ? RequestedMode.ParseSize(scaleText, scaleText) : null,
              Roi = roiText != null ? PixelRect.Parse(roiText) : null,
            };
            var runOptions = new CaptureRunOptions
            {
              OutputDirectory = output,
              Duration = DurationParser.ParseOptional(parseResult.GetValue(durationOption)),
              WaitForStart = parseResult.GetValue(waitOption),
              StopAtEnd = parseResult.GetValue(stopOption),
              ToolVersion = Program.VersionString,
              FfmpegVersion = FfmpegDevices.GetVersion(ffmpeg),
              FfmpegCommandLine = string.Join(" ", FfmpegCommandBuilder.BuildCapture(options)),
            };

            using var source = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30));
            var format = source.Format;
            AnsiConsole.MarkupLineInterpolated(
              $"Importing [bold]{media.Device.Name}[/] ({MediaInput.Classify(input)}): {format.SourceWidth}x{format.SourceHeight} -> stored {format.Width}x{format.Height} Gray8"
            );
            var result = await Task.Run(() => CaptureCommand.RunWithStatus(source, runOptions, cancellationToken), CancellationToken.None);
            CaptureCommand.PrintResult(result.Session);

            if (parseResult.GetValue(analyzeOption))
              return AnalyzeCommand.Run(result.Directory, new AnalysisOptions { ToolVersion = Program.VersionString });
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

    private static string DefaultOutputDirectory(FramePacingConfig config)
    {
      var root = config.CaptureDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mb-framepacing");
      return Path.Combine(root, $"import-{DateTime.Now:yyyyMMdd-HHmmss}");
    }
  }
}
