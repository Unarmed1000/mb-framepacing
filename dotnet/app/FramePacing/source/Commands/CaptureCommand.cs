//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'capture': record a capture card through ffmpeg into <dir>/frames.mbfc (+ capture.json), with a live status line. Ctrl+C stops.
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
  internal static class CaptureCommand
  {
    public static Command Create()
    {
      var deviceOption = new Option<string>("--device", "-d")
      {
        Description = "Capture device (see 'devices'), or 'lavfi:<graph>' for an ffmpeg test source, e.g. lavfi:testsrc2=size=1920x1080:rate=240.",
        Required = true,
      };
      var modeOption = new Option<string?>("--mode")
      {
        Description = "Device mode: WIDTHxHEIGHT@FPS, WIDTHxHEIGHT or @FPS (default: device default).",
      };
      var inputFormatOption = new Option<string?>("--input-format") { Description = "Device pixel format or codec: mjpeg, yuyv422, nv12, ..." };
      var scaleOption = new Option<string?>("--scale") { Description = "Stored frame size WIDTHxHEIGHT (area downscale). Prefer integer ratios." };
      var roiOption = new Option<string?>("--roi")
      {
        Description = "Only store this region of the source: x,y,width,height (source pixels, before --scale).",
      };
      var durationOption = new Option<string?>("--duration", "-t")
      {
        Description = "Stop after this long, e.g. 30s or 2m (default: until Ctrl+C or --stop-at-end).",
      };
      var waitOption = new Option<bool>("--wait-for-start")
      {
        Description = "Hold frames back until a start marker is seen (keeps a 250 ms pre-roll).",
      };
      var stopOption = new Option<bool>("--stop-at-end") { Description = "Stop once the end marker of the run has been seen." };
      var moduleOption = new Option<int?>("--module-px")
      {
        Description = "The marker module size the application uses (source pixels); checks it survives --scale.",
      };
      var ringOption = new Option<int?>("--ring-frames") { Description = "Recorder ring size in frames (default: one second, at most 512 MiB)." };
      var outputOption = new Option<string?>("--output", "-o")
      {
        Description =
          "Output directory (must not contain a capture yet). Default: a new capture-<date>-<time> folder in the configured capture directory.",
      };
      var analyzeOption = new Option<bool>("--analyze") { Description = "Run 'analyze' on the capture afterwards." };
      var ffmpegOption = CommonOptions.Ffmpeg();

      var command = new Command("capture", "Record a capture device to disk.")
      {
        deviceOption,
        modeOption,
        inputFormatOption,
        scaleOption,
        roiOption,
        durationOption,
        waitOption,
        stopOption,
        moduleOption,
        ringOption,
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
            var scaleText = parseResult.GetValue(scaleOption);
            var roiText = parseResult.GetValue(roiOption);
            var modeText = parseResult.GetValue(modeOption);
            var durationText = parseResult.GetValue(durationOption);
            var options = new FfmpegCaptureOptions
            {
              FfmpegPath = ffmpeg,
              Device = CaptureDevice.Parse(parseResult.GetValue(deviceOption)!),
              Mode = modeText != null ? RequestedMode.Parse(modeText) : default,
              InputFormat = parseResult.GetValue(inputFormatOption),
              Scale = scaleText != null ? RequestedMode.ParseSize(scaleText, scaleText) : null,
              Roi = roiText != null ? PixelRect.Parse(roiText) : null,
            };
            var runOptions = new CaptureRunOptions
            {
              OutputDirectory = Path.GetFullPath(parseResult.GetValue(outputOption) ?? DefaultOutputDirectory(config)),
              Duration = DurationParser.ParseOptional(durationText),
              WaitForStart = parseResult.GetValue(waitOption),
              StopAtEnd = parseResult.GetValue(stopOption),
              RingFrames = parseResult.GetValue(ringOption),
              ToolVersion = Program.VersionString,
              FfmpegVersion = FfmpegDevices.GetVersion(ffmpeg),
              FfmpegCommandLine = string.Join(" ", FfmpegCommandBuilder.BuildCapture(options)),
            };

            AnsiConsole.MarkupLineInterpolated($"[grey]{runOptions.FfmpegVersion}[/]");
            using var source = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(20));
            var format = source.Format;
            AnsiConsole.MarkupLineInterpolated(
              $"Capturing [bold]{options.Device.Name}[/]: source {format.SourceWidth}x{format.SourceHeight} @ {format.FrameRate} fps -> stored {format.Width}x{format.Height} Gray8"
            );
            CheckModuleSize(parseResult.GetValue(moduleOption), format);
            AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

            var result = await Task.Run(() => RunWithStatus(source, runOptions, cancellationToken), CancellationToken.None);
            PrintResult(result.Session);

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

    /// <summary>A new time stamped folder in the configured capture directory (default Documents/mb-framepacing).</summary>
    internal static string DefaultOutputDirectory(FramePacingConfig config)
    {
      var root = config.CaptureDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mb-framepacing");
      return Path.Combine(root, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}");
    }

    internal static CaptureResult RunWithStatus(ICaptureSource source, CaptureRunOptions options, CancellationToken cancellationToken)
    {
      CaptureResult? result = null;
      AnsiConsole
        .Status()
        .Spinner(Spinner.Known.Dots)
        .Start(
          "Starting...",
          context =>
          {
            result = CaptureRunner.Run(source, options, progress => context.Status(Describe(progress)), cancellationToken);
          }
        );
      return result!;
    }

    internal static void PrintResult(CaptureSessionInfo session)
    {
      var table = new Table().AddColumn("Capture").AddColumn(new TableColumn("Value").RightAligned());
      table.AddRow("Duration", $"{session.DurationSeconds:0.00} s");
      table.AddRow("Frames captured", session.FramesCaptured.ToString());
      table.AddRow("Frames written", session.FramesWritten.ToString());
      table.AddRow("Dropped by recorder", Highlight(session.FramesDroppedByRecorder));
      table.AddRow("Dropped by device/ffmpeg", Highlight(session.FramesDroppedBySource));
      if (session.WaitedForStart)
        table.AddRow("Discarded before start", session.FramesDiscardedBeforeStart.ToString());
      if (session.SequenceRunId.HasValue)
        table.AddRow("Run", Markup.Escape($"{session.SequenceRunId} '{session.SequenceName}'"));
      table.AddRow("Stopped by", Markup.Escape(session.StopReason));
      AnsiConsole.Write(table);
    }

    private static string Describe(CaptureProgress progress)
    {
      var r = progress.Recorder;
      double seconds = progress.Elapsed.TotalSeconds;
      double fps = seconds > 0 ? r.FramesCaptured / seconds : 0;
      double megabytesPerSecond = seconds > 0 ? r.BytesWritten / seconds / (1024 * 1024) : 0;
      var phase = progress.Phase switch
      {
        CapturePhase.WaitingForStart => "[yellow]waiting for start marker[/]",
        CapturePhase.Recording => "[green]recording[/]",
        CapturePhase.Stopping => "[grey]stopping[/]",
        _ => "done",
      };
      var marker = progress.LastMarker is { } m
        ? Markup.Escape($" | marker {m.Payload.Kind} run {m.Payload.RunId} frame {m.Payload.FrameIndex}")
        : string.Empty;
      return $"{phase} {seconds:0.0}s | {fps:0} fps | written {r.FramesWritten} ({megabytesPerSecond:0} MiB/s) | ring {r.RingFill}/{r.RingCapacity} | "
        + $"drops {Highlight(r.FramesDropped)}/{Highlight(progress.SourceDroppedFrames)}{marker}";
    }

    private static string Highlight(long value) => value > 0 ? $"[red]{value}[/]" : value.ToString();

    /// <summary>doc/marker-format.md "Sizing": warn below 3 stored pixels per module, error below 2.</summary>
    private static void CheckModuleSize(int? moduleSizePx, CaptureFormat format)
    {
      if (moduleSizePx is not { } module)
        return;
      int sourceHeight = format.Roi.IsEmpty ? format.SourceHeight : format.Roi.Height;
      if (sourceHeight <= 0)
      {
        AnsiConsole.MarkupLine("[yellow]The source size is unknown, the marker size can not be checked.[/]");
        return;
      }
      double stored = module * (double)format.Height / sourceHeight;
      int recommended = MarkerRenderer.RecommendModuleSizePx(sourceHeight, format.Height);
      if (stored < 2)
        throw new InvalidOperationException(
          $"A {module}px marker module becomes {stored:0.##} stored pixels, below the minimum of 2. Use --module-px {recommended} in the application or a larger --scale."
        );
      if (stored < 3)
        AnsiConsole.MarkupLineInterpolated(
          $"[yellow]A {module}px marker module becomes {stored:0.##} stored pixels (recommended 3+, i.e. {recommended}px).[/]"
        );
      else
        AnsiConsole.MarkupLineInterpolated($"[grey]Marker module: {module}px -> {stored:0.##} stored pixels.[/]");
    }
  }
}
