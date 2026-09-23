//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page: pick a capture, analyse it, show warnings, per run statistics and the per-frame data for the charts.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed record StatisticsRow(string Name, string Min, string Mean, string P50, string P95, string P99, string Max, string StdDev)
  {
    public static StatisticsRow From(string name, Statistics s) =>
      new StatisticsRow(name, F(s.Min), F(s.Mean), F(s.P50), F(s.P95), F(s.P99), F(s.Max), F(s.StdDev));

    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
  }

  public sealed class RunViewModel
  {
    public RunViewModel(RunAnalysis run, double capturePeriodMs)
    {
      Run = run;
      CapturePeriodMs = capturePeriodMs;
      Title = $"Run {run.RunId}" + (run.Name != null ? $"  '{run.Name}'" : string.Empty);
      var c = run.Counts;
      StartText = run.StartTimeUtc is { } start ? $"Started {start.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "No start time";
      CountsText =
        $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, "
        + $"{c.NotRecorded} not recorded. {c.SkippedFrameIndices} frame indices never captured, {c.Segments} segment(s).";
      ErrorFramesText =
        $"{run.Statistics.FramesWithAnimationError} frame(s) with |animation error| above the {capturePeriodMs.ToString("0.###", CultureInfo.InvariantCulture)} ms measurement resolution";
      var s = run.Statistics;
      PresentedFramesText = c.PresentedFrames.ToString("N0", CultureInfo.InvariantCulture);
      ErrorFramesTile = s.FramesWithAnimationError.ToString("N0", CultureInfo.InvariantCulture);
      HasErrorFrames = s.FramesWithAnimationError > 0;
      TypicalErrorText = s.AbsoluteAnimationErrorMs.P95.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      WorstErrorText = s.AbsoluteAnimationErrorMs.Max.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      ResolutionText = capturePeriodMs.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      Statistics = new List<StatisticsRow>
      {
        StatisticsRow.From("Display delta", s.DisplayDeltaMs),
        StatisticsRow.From("Animation delta", s.AnimationDeltaMs),
        StatisticsRow.From("Animation error", s.AnimationErrorMs),
        StatisticsRow.From("|Animation error|", s.AbsoluteAnimationErrorMs),
        StatisticsRow.From("Drift", s.DriftMs),
        StatisticsRow.From("On screen", s.OnScreenMs),
      };
    }

    public RunAnalysis Run { get; }
    public double CapturePeriodMs { get; }
    public string PresentedFramesText { get; }
    public string ErrorFramesTile { get; }
    public bool HasErrorFrames { get; }
    public string TypicalErrorText { get; }
    public string WorstErrorText { get; }
    public string ResolutionText { get; }
    public string Title { get; }
    public string StartText { get; }
    public string CountsText { get; }
    public string ErrorFramesText { get; }
    public IReadOnlyList<StatisticsRow> Statistics { get; }
    public IReadOnlyList<string> Warnings => Run.Warnings;
    public bool HasWarnings => Run.Warnings.Count > 0;

    public override string ToString() => Title;
  }

  public sealed partial class AnalysisViewModel : ObservableObject
  {
    private readonly IDialogService m_dialogs;

    public AnalysisViewModel(IDialogService dialogs, GuiSettings settings)
    {
      m_dialogs = dialogs;
      CaptureDirectory = settings.LastCaptureDirectory ?? string.Empty;
    }

    public IReadOnlyList<TimeSource> TimeSources { get; } = new[] { TimeSource.Auto, TimeSource.Device, TimeSource.Host };

    public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

    public ObservableCollection<RunViewModel> Runs { get; } = new ObservableCollection<RunViewModel>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial string CaptureDirectory { get; set; }

    [ObservableProperty]
    public partial TimeSource SelectedTimeSource { get; set; } = TimeSource.Auto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "Pick a capture folder and press Analyze.";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRun))]
    public partial RunViewModel? SelectedRun { get; set; }

    public bool IsIdle => !IsBusy;

    public bool HasRun => SelectedRun != null;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Analyse a capture right after it was recorded.</summary>
    public void AnalyzeDirectory(string directory)
    {
      CaptureDirectory = directory;
      if (AnalyzeCommand.CanExecute(null))
        AnalyzeCommand.Execute(null);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
      var path = await m_dialogs.PickFolderAsync("Select a capture folder (contains frames.mbfc)", CaptureDirectory);
      if (path != null)
        CaptureDirectory = path;
    }

    [RelayCommand]
    private void OpenReports()
    {
      if (!string.IsNullOrEmpty(ReportDirectory))
        m_dialogs.ShowInFileManager(ReportDirectory);
    }

    private bool CanAnalyze() => !IsBusy && !string.IsNullOrWhiteSpace(CaptureDirectory);

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
      IsBusy = true;
      ErrorText = string.Empty;
      ProgressPercent = 0;
      Warnings.Clear();
      Runs.Clear();
      SelectedRun = null;
      SummaryText = "Decoding markers...";
      try
      {
        var directory = CaptureDirectory.Trim();
        var options = new AnalysisOptions { TimeSource = SelectedTimeSource, ToolVersion = MainWindowViewModel.Version };
        var progress = new Progress<double>(value => ProgressPercent = value * 100);
        var report = await Task.Run(() => CaptureAnalyzer.Analyze(directory, options, progress));

        var layout = report.Capture.Layout;
        SummaryText =
          $"{report.Capture.Header.Width}x{report.Capture.Header.Height}, {report.Capture.Rows.Count} captures, capture period "
          + $"{report.CapturePeriodMs.ToString("0.###", CultureInfo.InvariantCulture)} ms ({report.Capture.TimeSource} clock), "
          + $"{layout.Locks.Count} marker(s) at {layout.ModuleSizePx.ToString("0.0", CultureInfo.InvariantCulture)} px/module";
        foreach (var warning in report.Warnings)
          Warnings.Add(warning);
        foreach (var run in report.Timeline.Runs)
          Runs.Add(new RunViewModel(run, report.CapturePeriodMs));
        SelectedRun = Runs.Count > 0 ? Runs[0] : null;
        ReportDirectory = report.OutputDirectory;
        ProgressPercent = 100;
      }
      catch (Exception ex)
      {
        SummaryText = "Analysis failed.";
        ErrorText = ex.Message;
      }
      finally
      {
        OnPropertyChanged(nameof(HasWarnings));
        IsBusy = false;
      }
    }
  }
}
