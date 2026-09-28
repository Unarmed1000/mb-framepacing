//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page: pick a capture, analyse it, show warnings, per run statistics and the per-frame data for the charts.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Analysis;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class AnalysisViewModel : ObservableObject
  {
    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;
    private AnalysisReport? m_report;

    public AnalysisViewModel(IDialogService dialogs, GuiSettings settings)
    {
      m_dialogs = dialogs;
      m_settings = settings;
      CaptureDirectory = settings.LastCaptureDirectory ?? string.Empty;
      if (Enum.TryParse(settings.TimeSource, out TimeSource timeSource))
        SelectedTimeSource = timeSource;
      TargetFpsText = settings.AnalysisTargetFps ?? string.Empty;
      DisplayHzText = settings.AnalysisDisplayHz ?? string.Empty;
    }

    /// <summary>Copy the current options into the settings (saved when the window closes).</summary>
    public void StoreSettings()
    {
      m_settings.TimeSource = SelectedTimeSource.ToString();
      m_settings.AnalysisTargetFps = TargetFpsText;
      m_settings.AnalysisDisplayHz = DisplayHzText;
      if (!string.IsNullOrWhiteSpace(CaptureDirectory))
        m_settings.LastCaptureDirectory = CaptureDirectory;
    }

    public IReadOnlyList<TimeSource> TimeSources { get; } = new[] { TimeSource.Auto, TimeSource.Device, TimeSource.Host };

    public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

    public ObservableCollection<RunViewModel> Runs { get; } = new ObservableCollection<RunViewModel>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial string CaptureDirectory { get; set; }

    [ObservableProperty]
    public partial TimeSource SelectedTimeSource { get; set; } = TimeSource.Auto;

    /// <summary>Overrides the capture's target frame rate (empty = the capture's own, or each run's median display time step).</summary>
    [ObservableProperty]
    public partial string TargetFpsText { get; set; }

    /// <summary>Overrides the capture's expected display refresh rate (empty = the capture's own, or no comparison).</summary>
    [ObservableProperty]
    public partial string DisplayHzText { get; set; }

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

    /// <summary>Write the charts of every run as PNG images next to the reports (the same files as 'analyze --charts').</summary>
    [RelayCommand]
    private async Task SaveChartsAsync()
    {
      if (m_report is not { } report)
        return;
      try
      {
        var files = await Task.Run(() => ChartFiles.Write(report, ChartTheme.Light));
        ErrorText = string.Empty;
        SummaryText = $"{files.Count} chart image(s) written to {report.OutputDirectory}";
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        ErrorText = "Could not write the chart images: " + ex.Message;
      }
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
      m_report = null;
      SummaryText = "Decoding markers...";
      try
      {
        var directory = CaptureDirectory.Trim();
        var options = new AnalysisOptions
        {
          TimeSource = SelectedTimeSource,
          Timeline = new TimelineOptions
          {
            TargetFps = FrameRateText.ParseOptional(TargetFpsText),
            ExpectedRefreshHz = FrameRateText.ParseOptional(DisplayHzText),
          },
          ToolVersion = MainWindowViewModel.Version,
        };
        var progress = new Progress<double>(value => ProgressPercent = value * 100);
        var report = await Task.Run(() =>
        {
          return CaptureAnalyzer.Analyze(directory, options, progress);
        });

        var layout = report.Capture.Layout;
        SummaryText =
          $"{report.Capture.Header.Width}x{report.Capture.Header.Height}, {report.Capture.Rows.Count} captures, capture period "
          + $"{report.CapturePeriodMs.ToString("0.###", CultureInfo.InvariantCulture)} ms ({report.Capture.TimeSource} clock), "
          + $"{layout.Locks.Count} marker(s) at {layout.ModuleSizePx.ToString("0.0", CultureInfo.InvariantCulture)} px/module";
        foreach (var warning in report.Warnings)
          Warnings.Add(warning);
        foreach (var run in report.Timeline.Runs)
          Runs.Add(new RunViewModel(ChartRun.From(report, run)));
        SelectedRun = Runs.Count > 0 ? Runs[0] : null;
        ReportDirectory = report.OutputDirectory;
        m_report = report;
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
