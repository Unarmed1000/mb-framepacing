//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* DocImages: renders the images used by README.md and doc/ without touching the desktop.
//*   - GUI screenshots: the real GUI runs in Avalonia's headless platform with the Skia renderer (offscreen), imports and analyses a test
//*     clip (measure/test-data/videos, through ffmpeg), and each page is saved as PNG. Machine specific text (paths) is replaced with
//*     neutral example values first.
//*   - Marker examples: the start, frame and end markers as an application draws them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MB.FramePacing.Capture;
using MB.FramePacing.Charts;
using MB.FramePacing.Gui;
using MB.FramePacing.Gui.ViewModels;
using MB.FramePacing.Gui.Views;

namespace MB.FramePacing.DocImages
{
  internal static class Program
  {
    private const string ExampleCaptureRoot = @"D:\captures";
    private const string ExampleFfmpeg = @"C:\ffmpeg\bin\ffmpeg.exe";
    private const string ExampleRecording = @"D:\recordings\capture-card-240hz.mkv";

    /// <summary>The test clip the GUI screenshots import and analyse: a game that adapts its rate to a busy stretch.</summary>
    private const string ScreenshotClip = "60-busy-adaptive";

    private static int Main(string[] args)
    {
      var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(FindRepositoryRoot(), "measure", "doc", "images"));
      Directory.CreateDirectory(output);
      var work = Path.Combine(Path.GetTempPath(), "mb-framepacing-docimages-" + Guid.NewGuid().ToString("N"));
      try
      {
        // A log file from long ago: starting the GUI deletes it
        string oldLog = Path.Combine(work, "logs", GuiLogging.FilePrefix + "2000-01-01.log");
        Directory.CreateDirectory(Path.GetDirectoryName(oldLog)!);
        File.WriteAllText(oldLog, string.Empty);
        // Drive the GUI like --output-root: captures go to a temporary folder and no user setting is ever loaded or saved
        MB.FramePacing.Gui.Program.ConfigureForAutomation(work);
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        session.Dispatch(() => RenderGuiAsync(output), CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"GUI screenshots written to {output}");
        CheckLog(work, oldLog);
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

    /// <summary>The GUI logged into the output root's logs folder (never the user's), and deleted the log files older than a week there.</summary>
    private static void CheckLog(string work, string oldLog)
    {
      string directory = Path.Combine(work, "logs");
      if (GuiLogging.Directory != directory)
        throw new InvalidOperationException($"The GUI logs to {GuiLogging.Directory}, not to the output root's {directory}");
      NLog.LogManager.Shutdown(); // closes the file the log keeps open
      string today = Path.Combine(
        directory,
        GuiLogging.FilePrefix + DateTime.Now.ToString(GuiLogging.DateFormat, CultureInfo.InvariantCulture) + ".log"
      );
      string log = File.Exists(today) ? File.ReadAllText(today) : string.Empty;
      if (!log.Contains("mb-framepacing-gui", StringComparison.Ordinal))
        throw new InvalidOperationException($"No log in {today}");
      // The libraries log through the same file: the camera wizard's calibration says what it measured the refresh rate from
      string? calibration = log.Split('\n').FirstOrDefault(line => line.Contains("Calibration timing", StringComparison.Ordinal));
      if (calibration == null)
        throw new InvalidOperationException($"The camera calibration is not in {today}");
      Console.WriteLine(calibration.Trim());
      if (File.Exists(oldLog))
        throw new InvalidOperationException($"The old log file {oldLog} was not deleted");
      Console.WriteLine($"GUI log checked: {today}");
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
      await viewModel.InitializeAsync();
      var capture = viewModel.Capture;
      if (!capture.FfmpegReady)
        throw new InvalidOperationException(
          "The GUI screenshots import a test clip through ffmpeg: install ffmpeg, or set it with mb-framepacing config"
        );

      // The capture page set up the suggested way: a recording of the capture card, imported as a video file
      capture.SelectedDevice = capture.Devices.First(d => d.Kind == SourceKind.VideoFile);
      capture.DisplayHzText = "60";
      capture.MediaPath = ExampleRecording;
      capture.OutputRoot = ExampleCaptureRoot;
      capture.FfmpegSummary = "ffmpeg 9.0.2";
      await Task.Delay(300);
      Save(window, Path.Combine(output, "gui-capture.png"));

      // Import the test clip itself: the import finishes by itself, and the analysis page opens and analyses it
      capture.MediaPath = Path.Combine(FindRepositoryRoot(), "measure", "test-data", "videos", ScreenshotClip, "video.mp4");
      capture.OutputRoot = MB.FramePacing.Gui.Program.OutputRoot!;
      capture.StartCommand.Execute(null);
      await WaitUntil(() => viewModel.Analysis.HasRun && !viewModel.Analysis.IsBusy, TimeSpan.FromSeconds(60));
      viewModel.Analysis.CaptureDirectory = Path.Combine(ExampleCaptureRoot, "capture-20260923-120000");
      await Task.Delay(300);
      Save(window, Path.Combine(output, "gui-analysis.png"));

      await CheckTimelineInteractionAsync(window, viewModel.Analysis);

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

    /// <summary>
    /// The Timeline card with real (headless) input, its cards built in the background: it is laid out for its width; wheel notches in quick
    /// succession add up, around the time under the pointer; zoomed, it is a sliding window: a sideways wheel, the scrollbar and a drag only
    /// move it (the same card, the scrollbar following), and scrolling near its edge brings the next window; a double-click shows the whole run
    /// again; hovering a plot names the frame under the pointer. Throws when one does not; no picture.
    /// </summary>
    private static async Task CheckTimelineInteractionAsync(Window window, AnalysisViewModel analysis)
    {
      var card = window.GetVisualDescendants().OfType<CardView>().Single(v => v.Name == "TimelineCardView");
      // The first plot as shown: its time range moved with the sliding window
      CardPlot Shown()
      {
        var plot = card.Drawing!.Plots[0];
        double seconds = card.ScrollOffset * (plot.XTo - plot.XFrom) / (plot.Right - plot.Left);
        return plot with { XFrom = plot.XFrom - seconds, XTo = plot.XTo - seconds };
      }
      async Task Settle(string what)
      {
        try
        {
          await WaitUntil(() => card.Drawing != null && !analysis.IsBuildingCards, TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
          throw new InvalidOperationException($"Timeline card: {what}: the cards were not built ({analysis.SectionText})");
        }
      }
      async Task<CardPlot> Built(CardDrawing? before, string what)
      {
        try
        {
          await WaitUntil(() => card.Drawing != null && card.Drawing != before && !analysis.IsBuildingCards, TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
          throw new InvalidOperationException($"Timeline card: {what}: no new card ({analysis.SectionText}; {analysis.ErrorText})");
        }
        return Shown();
      }
      void Expect(bool condition, string what)
      {
        if (!condition)
          throw new InvalidOperationException($"Timeline card: {what} ({analysis.SectionText})");
      }

      await Settle("the first card");
      var drawing = card.Drawing!;
      Expect(Math.Abs(drawing.Width - Math.Max(AnalysisViewModel.MinCardWidth, Math.Floor(card.Bounds.Width))) < 1, "laid out for its width");
      var plot = drawing.Plots[0];
      double scale = card.Bounds.Width / drawing.Width;
      Avalonia.Point At(double x, double y) =>
        card.TranslatePoint(new Avalonia.Point(x * scale, y * scale), window) ?? throw new InvalidOperationException("The card is not in the window");
      var middle = At((plot.Left + plot.Right) / 2, (plot.Top + plot.Bottom) / 2);
      double atPointer = plot.ValueX((plot.Left + plot.Right) / 2);
      string whole = analysis.SectionText;

      window.MouseMove(middle);
      Dispatcher.UIThread.RunJobs();
      Expect(card.Drawing == drawing, "hovering does not change the card");
      // Three notches before a card arrives: they add up
      window.MouseWheel(middle, new Vector(0, 1));
      window.MouseWheel(middle, new Vector(0, 1));
      window.MouseWheel(middle, new Vector(0, 1));
      var zoomed = await Built(drawing, "the wheel zooms in");
      double length = zoomed.XTo - zoomed.XFrom;
      Expect(Math.Abs(length - ((plot.XTo - plot.XFrom) * 0.8 * 0.8 * 0.8)) < 1e-6, "three notches zoom three times");
      Expect(Math.Abs(zoomed.ValueX((zoomed.Left + zoomed.Right) / 2) - atPointer) < 1e-6, "the time under the pointer stays there");
      Expect(analysis.CanScroll && Math.Abs(analysis.ScrollValue - zoomed.XFrom) < 1e-9, "the scrollbar shows where the view is");

      // Zoomed in further, the window is a small part of the run (the card before the input: the new one may arrive with it)
      var beforeFurther = card.Drawing;
      for (int i = 0; i < 10; ++i)
        window.MouseWheel(middle, new Vector(0, 1));
      zoomed = await Built(beforeFurther, "the wheel zooms in further");
      length = zoomed.XTo - zoomed.XFrom;
      var window0 = card.Drawing;

      // Sideways: two notches to the right, a fifth of the view; within the window only the card moves
      window.MouseWheel(middle, new Vector(-2, 0));
      Dispatcher.UIThread.RunJobs();
      var scrolled = Shown();
      Expect(card.Drawing == window0 && card.ScrollOffset < 0, "a sideways wheel within the window moves the card, nothing is built");
      Expect(Math.Abs(scrolled.XFrom - (zoomed.XFrom + (length * 0.2))) < 1e-6, "a sideways wheel scrolls a tenth of the view per notch");
      Expect(Math.Abs(analysis.ScrollValue - scrolled.XFrom) < 1e-9, "the scrollbar follows");
      await Settle("the distribution cards follow the scroll");
      Expect(card.Drawing == window0, "the distribution cards follow without a new Timeline card");

      analysis.ScrollValue = zoomed.XFrom;
      Dispatcher.UIThread.RunJobs();
      Expect(card.Drawing == window0 && Math.Abs(Shown().XFrom - zoomed.XFrom) < 1e-6, "the scrollbar moves the window back");

      var right = At((plot.Left + plot.Right) / 2 + 100, (plot.Top + plot.Bottom) / 2);
      window.MouseDown(middle, MouseButton.Left);
      window.MouseMove(right);
      window.MouseUp(right, MouseButton.Left);
      Dispatcher.UIThread.RunJobs();
      var panned = Shown();
      Expect(card.Drawing == window0, "a drag within the window moves the card");
      Expect(panned.XFrom < zoomed.XFrom && Math.Abs((panned.XTo - panned.XFrom) - length) < 1e-6, "dragging right pans back");
      await Settle("after the drag");

      // Seven notches to the right from where it was built: within half a screen of the window's edge, the next window is built
      analysis.ScrollValue = zoomed.XFrom;
      var before = card.Drawing;
      window.MouseWheel(middle, new Vector(-7, 0));
      var slid = await Built(before, "scrolling near the window's edge builds the next one");
      Expect(Math.Abs(slid.XFrom - (zoomed.XFrom + (length * 0.7))) < 1e-6, "the next window shows the view scrolled to");

      before = card.Drawing;
      window.MouseDown(middle, MouseButton.Left);
      window.MouseUp(middle, MouseButton.Left);
      window.MouseDown(middle, MouseButton.Left);
      window.MouseUp(middle, MouseButton.Left);
      _ = await Built(before, "a double-click resets");
      Expect(analysis.SectionText == whole && !analysis.CanScroll && card.ScrollOffset == 0, "a double-click shows the whole run");
      window.MouseMove(middle);
      Dispatcher.UIThread.RunJobs();
      var frame = new MB.FramePacing.Charts.CardHover(RunSection.Whole(analysis.SelectedRun!.Chart)).FrameAt(atPointer);
      Expect(
        analysis.HoverText(card.Drawing!.Plots[0], atPointer, 0)?.StartsWith($"Frame {frame!.FrameIndex} at", StringComparison.Ordinal) == true,
        "hovering names the frame under the pointer"
      );
      // The Clamp static switch builds the Timeline again, and the setting comes back on
      Expect(analysis.ClampStatic, "Clamp static is on by default");
      before = card.Drawing;
      analysis.ClampStatic = false;
      _ = await Built(before, "switching Clamp static off builds the Timeline again");
      before = card.Drawing;
      analysis.ClampStatic = true;
      _ = await Built(before, "switching Clamp static on builds the Timeline again");
      Console.WriteLine(
        "  Timeline card: width, wheel zoom, sliding window (sideways scroll, scrollbar, drag, next window), double-click, hover and Clamp "
          + "static work"
      );
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
    /// The SVG report examples of the README, from test clips made by mb-framepacing-explained (measure/test-data/videos): a game adapting its
    /// rate, a busy stretch at the full rate, delta time jitter from a naive timer, and the perfect storm (the jitter and late frames at
    /// once). Imported through ffmpeg; skipped without it.
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
      foreach (
        var (clip, name) in new[]
        {
          ("60-busy-adaptive", "adaptive"),
          ("60-busy-full-rate", "busy"),
          ("60-naive-5ms", "jitter"),
          ("60-naive-5ms-diagram-slow-frames-every-1s", "storm"),
        }
      )
      {
        string video = Path.Combine(FindRepositoryRoot(), "measure", "test-data", "videos", clip, "video.mp4");
        string imported = Path.Combine(work, clip);
        var media = MB.FramePacing.Capture.Ffmpeg.MediaInput.Create(video, new MB.FramePacing.Capture.Ffmpeg.MediaInputOptions(), imported);
        using (var source = MB.FramePacing.Capture.Ffmpeg.FfmpegCaptureSource.Start(media.ToCaptureOptions(ffmpeg), TimeSpan.FromSeconds(30)))
          CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = imported }, null, CancellationToken.None);
        WriteReport(
          imported,
          Path.Combine(output, $"report-example-{name}.svg"),
          name == "busy" ? Path.Combine(output, "timeline-example-busy.svg") : null,
          // The distribution cards of the README's "reading the results": the errors of a naive timer, the display time steps of a
          // game adapting its rate
          name switch
          {
            "jitter" => new[] { DistributionCard.ErrorHistogram, DistributionCard.ErrorPercentiles },
            "adaptive" => new[] { DistributionCard.DisplayTimeStepHistogram },
            _ => Array.Empty<string>(),
          },
          output
        );
      }
    }

    /// <summary>
    /// Analyse the capture and write its (only) run's report, the frame timeline of the start of its busy stretch if asked, and the
    /// distribution cards <paramref name="cards"/> as chart-&lt;card&gt;.svg in <paramref name="output"/>.
    /// </summary>
    private static void WriteReport(string capture, string path, string? timelinePath, IReadOnlyList<string> cards, string output)
    {
      var report = MB.FramePacing.Analysis.CaptureAnalyzer.Analyze(capture, new MB.FramePacing.Analysis.AnalysisOptions());
      var chart = ChartRun.From(report, report.Timeline.Runs.Single());
      File.WriteAllText(path, ReportCard.Render(RunSection.Whole(chart)), new System.Text.UTF8Encoding(false));
      Console.WriteLine($"  {Path.GetFileName(path)}");
      foreach (string card in cards)
      {
        string cardPath = Path.Combine(output, $"chart-{card}.svg");
        File.WriteAllText(cardPath, DistributionCard.Render(card, RunSection.Whole(chart)), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"  {Path.GetFileName(cardPath)}");
      }
      if (timelinePath == null)
        return;
      File.WriteAllText(timelinePath, FrameTimelineCard.Render(RunSection.Create(chart, 1.9, 2.25)), new System.Text.UTF8Encoding(false));
      Console.WriteLine($"  {Path.GetFileName(timelinePath)}");
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
