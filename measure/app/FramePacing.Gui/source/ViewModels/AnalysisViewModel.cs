//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page: pick a capture, analyse it, show warnings, per run statistics and the report cards of the selected run. The cards show one
//* section of the run (all of it at first): zooming and panning the Timeline card chooses it, and the distribution cards follow.
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
    private RunSection? m_section;
    private CardHover? m_hover;

    /// <summary>The Timeline card shows the panels only: the page has its own tiles and title.</summary>
    public static readonly ReportOptions TimelineOptions = ReportOptions.ShowOnly(
      new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.FrameTime, ReportItem.LateShare, ReportItem.RefreshStrip }
    );

    /// <summary>The cards in the order of the page's tabs: the Timeline (the report's panels), then the distributions.</summary>
    public static readonly IReadOnlyList<string> CardIds = new[]
    {
      "report",
      DistributionCard.ErrorHistogram,
      DistributionCard.ErrorPercentiles,
      DistributionCard.DisplayTimeStepHistogram,
      DistributionCard.Drift,
    };

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

    /// <summary>The Timeline card of the section.</summary>
    [ObservableProperty]
    public partial CardDrawing? TimelineCard { get; set; }

    [ObservableProperty]
    public partial CardDrawing? ErrorHistogramCard { get; set; }

    [ObservableProperty]
    public partial CardDrawing? ErrorPercentilesCard { get; set; }

    [ObservableProperty]
    public partial CardDrawing? DisplayTimeStepHistogramCard { get; set; }

    [ObservableProperty]
    public partial CardDrawing? DriftCard { get; set; }

    /// <summary>The tab shown: an index into <see cref="CardIds"/>.</summary>
    [ObservableProperty]
    public partial int SelectedCardIndex { get; set; }

    /// <summary>Which part of the run the cards show.</summary>
    [ObservableProperty]
    public partial string SectionText { get; set; } = string.Empty;

    public bool IsIdle => !IsBusy;

    public bool HasRun => SelectedRun != null;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Show <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> of the selected run (seconds since its first frame), kept inside the
    /// run and at least a few capture periods long.</summary>
    public void ShowRange(double fromSeconds, double toSeconds)
    {
      if (SelectedRun?.Chart is not { } chart)
        return;
      var whole = RunSection.Whole(chart);
      double shortest = Math.Min(whole.ToSeconds, Math.Max(0.01, 4.0 * chart.CapturePeriodTicks / TimeSpan.TicksPerSecond));
      double length = Math.Clamp(toSeconds - fromSeconds, shortest, whole.ToSeconds);
      double from = Math.Clamp(fromSeconds, 0, whole.ToSeconds - length);
      ShowSection(from <= 0 && length >= whole.ToSeconds ? whole : RunSection.Create(chart, from, from + length));
    }

    /// <summary>Back to the whole run.</summary>
    [RelayCommand]
    private void ResetRange()
    {
      if (SelectedRun?.Chart is { } chart)
        ShowSection(RunSection.Whole(chart));
    }

    /// <summary>What the cards show at a point of one of their plots (the frame, the bin, the percentile).</summary>
    public string? HoverText(CardPlot plot, double x, double y) => m_hover?.Describe(plot, x, y);

    /// <summary>Save the card on screen, as it is zoomed, as SVG or PNG.</summary>
    [RelayCommand]
    private async Task SaveViewAsync()
    {
      var card = SelectedCardIndex switch
      {
        0 => TimelineCard,
        1 => ErrorHistogramCard,
        2 => ErrorPercentilesCard,
        3 => DisplayTimeStepHistogramCard,
        _ => DriftCard,
      };
      if (card == null || m_section is not { } section)
        return;
      string suffix = section.IsWholeRun
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"-{section.FromSeconds:0.###}s-{section.ToSeconds:0.###}s");
      string name = $"run-{section.Run.Run.RunId}-{CardIds[Math.Clamp(SelectedCardIndex, 0, CardIds.Count - 1)]}{suffix}";
      var path = await m_dialogs.SaveFileAsync("Save the chart as it is shown", name, new[] { ("SVG image", "svg"), ("PNG image", "png") });
      if (path == null)
        return;
      try
      {
        CardImage.Save(card, path);
        ErrorText = string.Empty;
        SummaryText = $"Saved {path}";
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        ErrorText = "Could not save the chart: " + ex.Message;
      }
    }

    /// <summary>A new run keeps the range shown when the run has it, else shows all of itself.</summary>
    partial void OnSelectedRunChanged(RunViewModel? value)
    {
      if (value?.Chart is not { } chart)
      {
        ShowSection(null);
        return;
      }
      var whole = RunSection.Whole(chart);
      bool keep = m_section is { IsWholeRun: false } shown && shown.ToSeconds <= whole.ToSeconds;
      ShowSection(keep ? RunSection.Create(chart, m_section!.FromSeconds, m_section.ToSeconds) : whole);
    }

    private void ShowSection(RunSection? section)
    {
      m_section = section;
      m_hover = section != null ? new CardHover(section) : null;
      TimelineCard = section != null ? ReportCard.Build(section, TimelineOptions) : null;
      ErrorHistogramCard = section != null ? DistributionCard.Build(DistributionCard.ErrorHistogram, section) : null;
      ErrorPercentilesCard = section != null ? DistributionCard.Build(DistributionCard.ErrorPercentiles, section) : null;
      DisplayTimeStepHistogramCard = section != null ? DistributionCard.Build(DistributionCard.DisplayTimeStepHistogram, section) : null;
      DriftCard = section != null ? DistributionCard.Build(DistributionCard.Drift, section) : null;
      SectionText = section switch
      {
        null => string.Empty,
        { IsWholeRun: true } => string.Create(CultureInfo.InvariantCulture, $"The whole run: {section.ToSeconds:0.0} s"),
        _ => string.Create(
          CultureInfo.InvariantCulture,
          $"{section.FromSeconds:0.000}–{section.ToSeconds:0.000} s of {RunSection.Whole(section.Run).ToSeconds:0.0} s"
        ),
      };
    }

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
      var path = await m_dialogs.PickFolderAsync("Select a capture folder (contains captures.mbcd or frames.mbfc)", CaptureDirectory);
      if (path != null)
        CaptureDirectory = path;
    }

    [RelayCommand]
    private void OpenReports()
    {
      if (!string.IsNullOrEmpty(ReportDirectory))
        m_dialogs.ShowInFileManager(ReportDirectory);
    }

    /// <summary>Write the charts of every run as SVG cards next to the reports (the same files as 'analyze --charts').</summary>
    [RelayCommand]
    private async Task SaveChartsAsync()
    {
      if (m_report is not { } report)
        return;
      try
      {
        var files = await Task.Run(() => ChartFiles.Write(report));
        ErrorText = string.Empty;
        SummaryText = $"{files.Count} chart(s) written to {report.OutputDirectory}";
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        ErrorText = "Could not write the charts: " + ex.Message;
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
