//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* DocImages: renders the images used by README.md and doc/ without touching the desktop.
//*   - GUI screenshots: the real GUI runs in Avalonia's headless platform with the Skia renderer (offscreen), captures and analyses the
//*     synthetic test game, and each page is saved as PNG. Machine specific text (paths) is replaced with neutral example values first.
//*   - Marker examples: how the markers look inside an application frame.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MB.FramePacing.Capture;
using MB.FramePacing.Charts;
using MB.FramePacing.Gui;
using MB.FramePacing.Gui.ViewModels;
using MB.FramePacing.Gui.Views;
using ScottPlot.Avalonia;

namespace MB.FramePacing.DocImages
{
  internal static class Program
  {
    private const string ExampleCaptureRoot = @"D:\captures";
    private const string ExampleFfmpeg = @"C:\ffmpeg\bin\ffmpeg.exe";

    private static int Main(string[] args)
    {
      var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(FindRepositoryRoot(), "doc", "images"));
      Directory.CreateDirectory(output);
      var work = Path.Combine(Path.GetTempPath(), "mb-framepacing-docimages-" + Guid.NewGuid().ToString("N"));
      try
      {
        // Drive the GUI like --demo --output-root: captures go to a temporary folder and no user setting is ever saved
        MB.FramePacing.Gui.Program.ConfigureForAutomation(work);
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        session.Dispatch(() => RenderGuiAsync(output), CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"GUI screenshots written to {output}");
        WriteReportExamples(output, Path.Combine(work, "reports"));
        return 0;
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine("DocImages failed: " + ex);
        return 1;
      }
      finally
      {
        try
        {
          if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        }
        catch (IOException) { }
      }
    }

    private static async Task<bool> RenderGuiAsync(string output)
    {
      Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
      MarkerImages.WriteAll(output);
      Console.WriteLine("Marker images written");

      var window = new MainWindow();
      var viewModel = new MainWindowViewModel(new DialogService(window));
      window.DataContext = viewModel;
      window.Show();
      await viewModel.InitializeAsync(); // --demo mode: starts capturing the synthetic test game

      // Capture page while recording: wait for a live picture and a marker
      await WaitUntil(
        () => viewModel.Capture.HasPreview && viewModel.Capture.MarkerText.Contains("Frame", StringComparison.Ordinal),
        TimeSpan.FromSeconds(20)
      );
      await Task.Delay(700);
      viewModel.Capture.OutputRoot = ExampleCaptureRoot;
      viewModel.Capture.FfmpegSummary = "ffmpeg 9.0.2";
      Save(window, Path.Combine(output, "gui-capture.png"));

      // The capture finishes by itself; the analysis page opens and analyses it
      await WaitUntil(() => viewModel.Analysis.HasRun && !viewModel.Analysis.IsBusy, TimeSpan.FromSeconds(60));
      viewModel.Analysis.CaptureDirectory = Path.Combine(ExampleCaptureRoot, "capture-20260923-120000");
      await Task.Delay(300);
      Save(window, Path.Combine(output, "gui-analysis.png"));

      // The distribution charts on their own, for the README's "reading the results"
      var analysisView = window.GetVisualDescendants().OfType<AnalysisView>().Single();
      foreach (
        var (name, file) in new[]
        {
          ("ErrorHistogramPlot", "chart-error-histogram.png"),
          ("ErrorPercentilePlot", "chart-error-percentiles.png"),
          ("DisplayTimeStepHistogramPlot", "chart-display-time-step-histogram.png"),
        }
      )
      {
        analysisView.FindControl<AvaPlot>(name)!.Plot.SavePng(Path.Combine(output, file), 900, 400);
        Console.WriteLine($"  {file} (900x400)");
      }

      // The setup dialog as a user without ffmpeg sees it after pressing 'Find automatically'
      var setupViewModel = new SetupViewModel(
        new DialogService(window),
        new MB.FramePacing.Capture.FramePacingConfig(),
        ExampleCaptureRoot,
        firstRun: true
      );
      await WaitUntil(() => !setupViewModel.IsChecking, TimeSpan.FromSeconds(10));
      setupViewModel.FfmpegPath = ExampleFfmpeg;
      setupViewModel.FfmpegStatus = "✓ Found ffmpeg 9.0.2";
      setupViewModel.FfmpegProblem = false;
      setupViewModel.FfmpegOk = true;
      var setup = new SetupWindow { DataContext = setupViewModel };
      setup.Show();
      await Task.Delay(300);
      Save(setup, Path.Combine(output, "gui-setup.png"));
      setup.Close();

      await RenderCameraAsync(window, viewModel, output);
      window.Close();
      return true;
    }

    /// <summary>
    /// The camera wizard and card (VERY EXPERIMENTAL): set up the synthetic camera as a new camera (calibrate, save as "desk"), show that the
    /// next wizard offers the saved camera, then capture its rectified marker zones.
    /// </summary>
    private static async Task RenderCameraAsync(MainWindow window, MainWindowViewModel viewModel, string output)
    {
      var capture = viewModel.Capture;
      // Captures and saved cameras go to the automation folder, never to the example path shown in the screenshots
      capture.OutputRoot = MB.FramePacing.Gui.Program.OutputRoot!;
      viewModel.SelectedTab = MainWindowViewModel.CaptureTab;
      // The camera is an experimental feature: the Settings page's switch shows it (the other screenshots show the default, off)
      viewModel.Settings.ExperimentalFeatures = true;
      capture.SelectedDevice = capture.Devices.First(d => d.Kind == SourceKind.SyntheticCamera);

      var wizard = capture.CreateCameraWizard();
      var wizardWindow = new CameraWizardWindow { DataContext = wizard };
      wizardWindow.Show();
      wizard.IsNewCamera = true;
      wizard.NextCommand.Execute(null); // mount
      wizard.NextCommand.Execute(null); // source
      wizard.SelectedSource = wizard.Sources.First(s => s.Kind == SourceKind.SyntheticCamera);
      wizard.NextCommand.Execute(null); // calibrate
      await wizard.CalibrateCommand.ExecuteAsync(null);
      if (wizard.Rig == null)
        throw new InvalidOperationException("The synthetic camera did not calibrate: " + wizard.StatusText + wizard.ErrorText);
      // A fixed calibration time, so the images do not change with every run
      wizard.Rig = wizard.Rig with
      {
        CreatedUtc = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
      };
      await Task.Delay(300);
      Save(wizardWindow, Path.Combine(output, "gui-camera-wizard.png"));
      wizard.NextCommand.Execute(null); // save
      wizard.RigName = "desk";
      wizard.NextCommand.Execute(null); // done
      wizard.NextCommand.Execute(null); // finish
      capture.ApplyCameraWizardResult(wizard.Result ?? throw new InvalidOperationException("The wizard did not finish: " + wizard.ErrorText));

      // The next time the saved camera is offered and calibration is skipped
      var again = capture.CreateCameraWizard();
      var againWindow = new CameraWizardWindow { DataContext = again };
      againWindow.Show();
      await Task.Delay(300);
      Save(againWindow, Path.Combine(output, "gui-camera-wizard-saved.png"));
      again.CancelCommand.Execute(null);

      foreach (var expander in window.GetVisualDescendants().OfType<Expander>())
      {
        if (expander.Header is TextBlock { Text: { } header } && header.StartsWith("Camera", StringComparison.Ordinal))
          expander.IsExpanded = true;
      }
      capture.StartCommand.Execute(null);
      try
      {
        await WaitUntil(
          () => capture.HasPreview && capture.IsCapturing && capture.MarkerText.Contains("Frame", StringComparison.Ordinal),
          TimeSpan.FromSeconds(60)
        );
      }
      catch (TimeoutException)
      {
        throw new TimeoutException(
          $"The camera capture did not show a frame marker: phase '{capture.PhaseText}', marker '{capture.MarkerText}', preview {capture.HasPreview}, "
            + $"error '{capture.ErrorText}', camera '{capture.Camera.StatusText}'"
        );
      }
      await Task.Delay(700);
      capture.OutputRoot = ExampleCaptureRoot;
      Save(window, Path.Combine(output, "gui-camera.png"));
      capture.StopCommand.Execute(null);
      await WaitUntil(() => !capture.IsCapturing, TimeSpan.FromSeconds(60));
    }

    /// <summary>The Timeline tab's plots, one below the other, as one image.</summary>
    private static void Save(TopLevel topLevel, string path)
    {
      AvaloniaHeadlessPlatform.ForceRenderTimerTick();
      var frame = topLevel.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered");
      frame.Save(path, new PngBitmapEncoderOptions());
      Console.WriteLine($"  {Path.GetFileName(path)} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
      var deadline = DateTime.UtcNow + timeout;
      while (!condition())
      {
        if (DateTime.UtcNow > deadline)
          throw new TimeoutException("The GUI did not reach the expected state");
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
      }
    }

    /// <summary>
    /// The SVG report examples of the README, from test clips made by mb-framepacing-explained (test-data/videos): a game adapting its rate like
    /// Swappy, a busy stretch at the full rate, and delta time jitter from a naive timer. Imported through ffmpeg; skipped without it.
    /// </summary>
    private static void WriteReportExamples(string output, string work)
    {
      string ffmpeg;
      try
      {
        ffmpeg = MB.FramePacing.Capture.Ffmpeg.FfmpegLocator.Find(null, MB.FramePacing.Capture.FramePacingConfig.Load());
      }
      catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
      {
        Console.WriteLine("  report-example-*.svg skipped: ffmpeg is not installed");
        return;
      }
      foreach (var (clip, name) in new[] { ("60-busy-swappy", "swappy"), ("60-busy-full-rate", "busy"), ("60-naive-5ms", "jitter") })
      {
        string video = Path.Combine(FindRepositoryRoot(), "test-data", "videos", clip, "video.mp4");
        string imported = Path.Combine(work, clip);
        var media = MB.FramePacing.Capture.Ffmpeg.MediaInput.Create(video, new MB.FramePacing.Capture.Ffmpeg.MediaInputOptions(), imported);
        using (var source = MB.FramePacing.Capture.Ffmpeg.FfmpegCaptureSource.Start(media.ToCaptureOptions(ffmpeg), TimeSpan.FromSeconds(30)))
          CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = imported }, null, CancellationToken.None);
        WriteReport(imported, Path.Combine(output, $"report-example-{name}.svg"));
      }
    }

    /// <summary>Analyse the capture and write its (only) run's report.</summary>
    private static void WriteReport(string capture, string path)
    {
      var report = MB.FramePacing.Analysis.CaptureAnalyzer.Analyze(capture, new MB.FramePacing.Analysis.AnalysisOptions());
      var chart = ChartRun.From(report, report.Timeline.Runs.Single());
      File.WriteAllText(path, ReportSvg.Render(RunSection.Whole(chart)), new System.Text.UTF8Encoding(false));
      Console.WriteLine($"  {Path.GetFileName(path)}");
    }

    private static string FindRepositoryRoot()
    {
      var directory = new DirectoryInfo(AppContext.BaseDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "mb-framepacing.slnx")))
        directory = directory.Parent;
      return directory?.FullName ?? throw new DirectoryNotFoundException("Run DocImages from inside the repository or pass an output directory");
    }
  }
}
