//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Top level view model: header (ffmpeg status, settings), the capture and analysis pages and the first-run setup. A finished capture is
//* handed to the analysis page and analysed right away.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;
using NLog;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class MainWindowViewModel : ObservableObject
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    public const int CaptureTab = 0;
    public const int AnalysisTab = 1;
    public const int SettingsTab = 2;

    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;

    public MainWindowViewModel(IDialogService dialogs)
    {
      m_dialogs = dialogs;
      var settings = GuiSettings.Load();
      m_settings = settings;
      Capture = new CaptureViewModel(dialogs, settings);
      Analysis = new AnalysisViewModel(dialogs, settings);
      Settings = new SettingsViewModel(dialogs, Capture, () => ShowSetupAsync(firstRun: false));
      SelectedTab = settings.SelectedTab is CaptureTab or AnalysisTab or SettingsTab ? settings.SelectedTab : CaptureTab;
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

    public SettingsViewModel Settings { get; }

    /// <summary>Where captures go unless the configuration file names a folder.</summary>
    public static string DefaultCaptureDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mb-framepacing");

    public bool FfmpegReady => Capture.FfmpegReady;

    public string FfmpegSummary => Capture.FfmpegSummary;

    public string FfmpegToolTip => Capture.FfmpegToolTip;

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>Remember every option for the next start (not in automation or --output-root runs, see <see cref="GuiSettings.Save"/>).</summary>
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
      if (!Capture.FfmpegReady && !Program.Automation)
        await ShowSetupAsync(firstRun: true);
    }

    /// <summary>The header's Settings button: the Settings page.</summary>
    [RelayCommand]
    private void OpenSettings() => SelectedTab = SettingsTab;

    private async Task ShowSetupAsync(bool firstRun)
    {
      var setup = new SetupViewModel(m_dialogs, SafeLoadConfig(), DefaultCaptureDirectory, firstRun);
      if (await m_dialogs.ShowSetupAsync(setup))
        await Capture.RefreshDevicesAsync();
    }

    /// <summary>The configuration file, or the defaults when it can not be read.</summary>
    public static FramePacingConfig SafeLoadConfig()
    {
      try
      {
        return FramePacingConfig.Load();
      }
      catch (Exception ex)
      {
        g_logger.Warn("Could not read the configuration file, using the defaults: {0}", ex.Message);
        return new FramePacingConfig();
      }
    }
  }
}
