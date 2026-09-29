//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page: pick a capture, analyse it, show warnings, per run statistics and the report cards of the selected run. The cards show one
//* section of the run (all of it at first): zooming and panning the Timeline card chooses it, and the distribution cards follow. The cards
//* are built on the thread pool; only the latest request's are shown, and the cards on screen stay until they arrive. A zoomed Timeline is
//* a sliding window: built for a screen more on either side, so scrolling only moves it (TimelineOffset), and the next window is built in
//* the background when the view comes within half a screen of its edge. Every window at one zoom starts on a whole pixel column of the
//* run's time, so the next one draws the same columns and replaces the old one without a visible change.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Analysis;
using MB.FramePacing.Charts;
using NLog;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class AnalysisViewModel : ObservableObject
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;
    private AnalysisReport? m_report;
    private readonly LatestRequest<SectionCards> m_builds = new LatestRequest<SectionCards>();
    private readonly LatestRequest<SectionCards> m_follows = new LatestRequest<SectionCards>();
    private bool m_building;
    private bool m_following;
    private SectionCards? m_cards;
    private CardHover? m_hover;

    // The Timeline's sliding window: the latest one asked for, and the one on screen
    private TimelineWindow? m_window;
    private TimelineWindow? m_shownWindow;
    private CardHover? m_windowHover;

    // The range last asked for (null: the whole run): zooming builds on it, so wheel notches add up while a build runs
    private (double From, double To)? m_requested;

    // The cards' width in the window, in device independent pixels: one column per pixel (the files keep ReportCard.Width)
    private double m_cardWidth = ReportCard.Width;

    /// <summary>The narrowest card laid out for the window; a narrower window scales it down.</summary>
    public const double MinCardWidth = 640;

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

    /// <summary>The Timeline card of the section: a sliding window around it when zoomed.</summary>
    [ObservableProperty]
    public partial CardDrawing? TimelineCard { get; set; }

    /// <summary>How far the Timeline's sliding window is moved to the right (card units), so the view is in its plots.</summary>
    [ObservableProperty]
    public partial double TimelineOffset { get; set; }

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

    /// <summary>Cards are being built for a new section; the ones on screen stay until they are done.</summary>
    [ObservableProperty]
    public partial bool IsBuildingCards { get; set; }

    /// <summary>The scrollbar under the Timeline: the seconds in view, the last start there is, and the start (seconds since the first frame).</summary>
    [ObservableProperty]
    public partial double ViewSeconds { get; set; }

    [ObservableProperty]
    public partial double ScrollMaximum { get; set; }

    [ObservableProperty]
    public partial double ScrollValue { get; set; }

    /// <summary>Only part of the run is in view: the scrollbar shows.</summary>
    [ObservableProperty]
    public partial bool CanScroll { get; set; }

    private bool m_settingScroll;

    /// <summary>The scrollbar moved: show the same length from there.</summary>
    partial void OnScrollValueChanged(double value)
    {
      if (m_settingScroll || m_requested is not { } range)
        return;
      ShowRange(value, value + (range.To - range.From));
    }

    /// <summary>The cards' room in the window changed: lay them out for it.</summary>
    public void SetCardWidth(double width)
    {
      width = Math.Max(MinCardWidth, Math.Floor(width));
      if (Math.Abs(width - m_cardWidth) < 1)
        return;
      m_cardWidth = width;
      if (SelectedRun?.Chart is { } chart)
        Request(chart, m_requested);
    }

    /// <summary>Scroll by <paramref name="fraction"/> of the range in view (negative: to the left).</summary>
    public void Scroll(double fraction)
    {
      if (SelectedRun?.Chart is not { } chart || m_requested is not { } range)
        return;
      double shift = (range.To - range.From) * fraction;
      ShowRange(range.From + shift, range.To + shift);
    }

    public bool IsIdle => !IsBusy;

    public bool HasRun => SelectedRun != null;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Show <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> of the selected run (seconds since its first frame), kept inside the
    /// run and at least a few capture periods long.</summary>
    public void ShowRange(double fromSeconds, double toSeconds)
    {
      if (SelectedRun?.Chart is not { } chart)
        return;
      double whole = RunSection.Whole(chart).ToSeconds;
      double shortest = Math.Min(whole, Math.Max(0.01, 4.0 * chart.CapturePeriodTicks / TimeSpan.TicksPerSecond));
      double length = Math.Clamp(toSeconds - fromSeconds, shortest, whole);
      double from = Math.Clamp(fromSeconds, 0, whole - length);
      Request(chart, from <= 0 && length >= whole ? null : (from, from + length));
    }

    /// <summary>Zoom by <paramref name="factor"/> around <paramref name="atSeconds"/>: the range last asked for, scaled, the time kept in place.</summary>
    public void Zoom(double atSeconds, double factor)
    {
      if (SelectedRun?.Chart is not { } chart)
        return;
      var (from, to) = m_requested ?? (0, RunSection.Whole(chart).ToSeconds);
      ShowRange(atSeconds - ((atSeconds - from) * factor), atSeconds + ((to - atSeconds) * factor));
    }

    /// <summary>Back to the whole run.</summary>
    [RelayCommand]
    private void ResetRange()
    {
      if (SelectedRun?.Chart is { } chart)
        Request(chart, null);
    }

    /// <summary>What the cards show at a point of one of their plots (the frame, the bin, the percentile); the Timeline's frames come from its window.</summary>
    public string? HoverText(CardPlot plot, double x, double y) => (CardIds.Contains(plot.Id) ? m_hover : m_windowHover)?.Describe(plot, x, y);

    /// <summary>Save the card on screen, as it is zoomed, as SVG or PNG.</summary>
    [RelayCommand]
    private async Task SaveViewAsync()
    {
      if (SelectedRun?.Chart is not { } chart || TimelineCard == null)
        return;
      // Exactly the range in view (the Timeline on screen is a wider window), as wide as shown
      var section = ViewSection(chart, m_requested);
      string id = CardIds[Math.Clamp(SelectedCardIndex, 0, CardIds.Count - 1)];
      double width = TimelineCard.Width;
      var card =
        SelectedCardIndex == 0
          ? ReportCard.Build(section, TimelineOptions, wholeRunScales: true, width: width)
          : DistributionCard.Build(id, section, width);
      string suffix = section.IsWholeRun
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"-{section.FromSeconds:0.###}s-{section.ToSeconds:0.###}s");
      string name = $"run-{section.Run.Run.RunId}-{id}{suffix}";
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
        m_builds.Cancel();
        m_follows.Cancel();
        m_building = m_following = false;
        IsBuildingCards = false;
        m_requested = null;
        m_window = m_shownWindow = null;
        m_windowHover = null;
        Show(null);
        return;
      }
      bool keep = m_requested is { } range && range.To <= RunSection.Whole(chart).ToSeconds;
      Request(chart, keep ? m_requested : null);
    }

    /// <summary>
    /// Show <paramref name="range"/> (null: the whole run). Within the sliding window asked for last, at the same zoom and width, the window
    /// only moves (the next one is built when the view nears its edge) and the distribution cards follow; otherwise a new window is built.
    /// </summary>
    private void Request(ChartRun chart, (double From, double To)? range)
    {
      m_requested = range;
      UpdateScroll(chart, range);
      SectionText = SectionDescription(chart, range);
      if (range is { } view && m_window is { } window && window.Holds(chart, view, m_cardWidth))
      {
        MoveWindow();
        if (window.NearEdge(view))
          BuildWindow(chart, range);
        else
          Follow(chart, view);
        return;
      }
      BuildWindow(chart, range);
    }

    /// <summary>A new sliding window around <paramref name="range"/> with every card, on the thread pool; shown unless a newer one was asked for.</summary>
    private async void BuildWindow(ChartRun chart, (double From, double To)? range)
    {
      double width = m_cardWidth;
      var window = TimelineWindow.Around(chart, range, width);
      m_window = window;
      // This build brings the distribution cards of the range too
      m_follows.Cancel();
      m_following = false;
      m_building = true;
      IsBuildingCards = true;
      try
      {
        // The Timeline keeps the whole run's scales, so its axes stay while zooming and scrolling
        var cards = await m_builds.Run(token =>
          SectionCards.Build(ViewSection(chart, range), TimelineOptions, token, width, wholeRunScales: true, window.Card)
        );
        if (cards == null)
          return;
        m_building = false;
        m_shownWindow = window;
        m_windowHover = new CardHover(window.Section);
        TimelineCard = cards.Timeline;
        MoveWindow();
        Show(cards);
        // The view moved on while this one was built: its distribution cards
        if (m_requested is { } view && range != view)
          Follow(chart, view);
        UpdateBuilding();
      }
      catch (Exception ex)
      {
        g_logger.Error(ex, "Building the charts failed");
        ErrorText = "Could not draw the charts: " + ex.Message;
        m_building = false;
        UpdateBuilding();
      }
    }

    /// <summary>The distribution cards of <paramref name="view"/>, on the thread pool; the Timeline's window stays.</summary>
    private async void Follow(ChartRun chart, (double From, double To) view)
    {
      // Before the first window arrives there is nothing to follow: that build brings the cards
      if (m_building || m_cards is not { } cards)
        return;
      m_following = true;
      IsBuildingCards = true;
      try
      {
        var followed = await m_follows.Run(token => cards.Follow(ViewSection(chart, view), token));
        if (followed == null)
          return;
        m_following = false;
        Show(followed);
        UpdateBuilding();
      }
      catch (Exception ex)
      {
        g_logger.Error(ex, "Building the distribution cards failed");
        ErrorText = "Could not draw the charts: " + ex.Message;
        m_following = false;
        UpdateBuilding();
      }
    }

    /// <summary>Put the view in the Timeline's plots: the window on screen moved by the view's distance from where it was built to show.</summary>
    private void MoveWindow() => TimelineOffset = m_shownWindow is { } shown && m_requested is { } view ? shown.OffsetFor(view, m_cardWidth) : 0;

    private void UpdateBuilding() => IsBuildingCards = m_building || m_following;

    private static RunSection ViewSection(ChartRun chart, (double From, double To)? range) =>
      range is { } r ? RunSection.Create(chart, r.From, r.To) : RunSection.Whole(chart);

    private static string SectionDescription(ChartRun chart, (double From, double To)? range)
    {
      double whole = RunSection.Whole(chart).ToSeconds;
      return range is { } r
        ? string.Create(CultureInfo.InvariantCulture, $"{r.From:0.000}–{r.To:0.000} s of {whole:0.0} s")
        : string.Create(CultureInfo.InvariantCulture, $"The whole run: {whole:0.0} s");
    }

    /// <summary>The scrollbar at once, before the cards arrive: it follows what was asked for.</summary>
    private void UpdateScroll(ChartRun chart, (double From, double To)? range)
    {
      double whole = RunSection.Whole(chart).ToSeconds;
      var (from, to) = range ?? (0, whole);
      m_settingScroll = true;
      try
      {
        ViewSeconds = to - from;
        ScrollMaximum = Math.Max(0, whole - (to - from));
        ScrollValue = from;
        CanScroll = range != null;
      }
      finally
      {
        m_settingScroll = false;
      }
    }

    /// <summary>The distribution cards of the cards' section, and what hovering them says; nothing at all without cards.</summary>
    private void Show(SectionCards? cards)
    {
      m_cards = cards;
      m_hover = cards != null ? new CardHover(cards.Section) : null;
      if (cards == null)
      {
        TimelineCard = null;
        TimelineOffset = 0;
        SectionText = string.Empty;
      }
      ErrorHistogramCard = cards?.ErrorHistogram;
      ErrorPercentilesCard = cards?.ErrorPercentiles;
      DisplayTimeStepHistogramCard = cards?.DisplayTimeStepHistogram;
      DriftCard = cards?.Drift;
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
