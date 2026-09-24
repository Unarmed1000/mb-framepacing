//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Top level view model: header (ffmpeg status, settings), the capture and analysis pages and the first-run setup. A finished capture is
//* handed to the analysis page and analysed right away.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class MainWindowViewModel : ObservableObject
  {
    public const int CaptureTab = 0;
    public const int AnalysisTab = 1;

    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;

    public MainWindowViewModel(IDialogService dialogs)
    {
      m_dialogs = dialogs;
      var settings = GuiSettings.Load();
      m_settings = settings;
      Capture = new CaptureViewModel(dialogs, settings);
      Analysis = new AnalysisViewModel(dialogs, settings);
      SelectedTab = settings.SelectedTab is CaptureTab or AnalysisTab ? settings.SelectedTab : CaptureTab;
      Capture.CaptureCompleted += directory =>
      {
        SelectedTab = AnalysisTab;
        Analysis.AnalyzeDirectory(directory);
      };
      Capture.PropertyChanged += (_, e) =>
      {
        if (
          e.PropertyName is nameof(CaptureViewModel.FfmpegReady) or nameof(CaptureViewModel.FfmpegSummary) or nameof(CaptureViewModel.FfmpegToolTip)
        )
        {
          OnPropertyChanged(nameof(FfmpegReady));
          OnPropertyChanged(nameof(FfmpegSummary));
          OnPropertyChanged(nameof(FfmpegToolTip));
        }
      };
    }

    public string Title => $"mb-framepacing {Version}";

    public static string Version
    {
      get
      {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version.Substring(0, plus) : version;
      }
    }

    public CaptureViewModel Capture { get; }

    public AnalysisViewModel Analysis { get; }

    public bool FfmpegReady => Capture.FfmpegReady;

    public string FfmpegSummary => Capture.FfmpegSummary;

    public string FfmpegToolTip => Capture.FfmpegToolTip;

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>Remember every option for the next start (not in demo or --output-root runs, see <see cref="GuiSettings.Save"/>).</summary>
    public void SaveSettings()
    {
      Capture.StoreSettings();
      Analysis.StoreSettings();
      m_settings.SelectedTab = SelectedTab;
      m_settings.Save();
    }

    /// <summary>Called once the main window is shown: find ffmpeg and walk the user through the setup if it is missing.</summary>
    public async Task InitializeAsync()
    {
      await Capture.RefreshDevicesAsync();
      if (Program.Demo)
      {
        Capture.StartDemo();
        return;
      }
      if (!Capture.FfmpegReady)
        await ShowSetupAsync(firstRun: true);
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => ShowSetupAsync(firstRun: false);

    private async Task ShowSetupAsync(bool firstRun)
    {
      var defaultCaptures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mb-framepacing");
      var setup = new SetupViewModel(m_dialogs, SafeLoadConfig(), defaultCaptures, firstRun);
      if (await m_dialogs.ShowSetupAsync(setup))
        await Capture.RefreshDevicesAsync();
    }

    private static FramePacingConfig SafeLoadConfig()
    {
      try
      {
        return FramePacingConfig.Load();
      }
      catch (Exception)
      {
        return new FramePacingConfig();
      }
    }
  }
}
