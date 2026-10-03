//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The Settings page: ffmpeg and the captures folder (changed through the setup dialog, stored in the configuration file), the playback
//* page's answers about the recording (stored in the configuration file, shared with the command line), the switch for the experimental
//* features (stored in the GUI settings), and where both files are.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class SettingsViewModel : ObservableObject
  {
    private readonly IDialogService m_dialogs;
    private readonly CaptureViewModel m_capture;
    private readonly Func<Task> m_setUp;

    /// <param name="setUp">Opens the setup dialog (ffmpeg and the captures folder) and applies what it saved.</param>
    public SettingsViewModel(IDialogService dialogs, CaptureViewModel capture, Func<Task> setUp)
    {
      m_dialogs = dialogs;
      m_capture = capture;
      m_setUp = setUp;
      m_capture.PropertyChanged += OnCapturePropertyChanged;
      Refresh();
    }

    /// <summary>Show the experimental features (camera capture) on the Capture page.</summary>
    public bool ExperimentalFeatures
    {
      get => m_capture.ExperimentalFeatures;
      set => m_capture.ExperimentalFeatures = value;
    }

    public string ExperimentalText => CameraRigViewModel.ExperimentalText;

    public string FfmpegSummary => m_capture.FfmpegSummary;

    public bool FfmpegReady => m_capture.FfmpegReady;

    [ObservableProperty]
    public partial string FfmpegPathText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CaptureDirectoryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>The playback page and a recording browsers can play, as <see cref="PlaybackVideoChoice"/> (ask, copy, link).</summary>
    public IReadOnlyList<string> PlaybackVideoChoices { get; } = new[] { "Ask every time", "Copy it into the playback folder", "Link the recording" };

    /// <summary>The playback page and a recording browsers cannot play, as <see cref="PlaybackTranscodeChoice"/> (ask, yes, no).</summary>
    public IReadOnlyList<string> PlaybackTranscodeChoices { get; } = new[] { "Ask every time", "Make a playable copy", "Link it as it is" };

    [ObservableProperty]
    public partial int PlaybackVideoIndex { get; set; }

    [ObservableProperty]
    public partial int PlaybackTranscodeIndex { get; set; }

    [ObservableProperty]
    public partial string PlaybackStatusText { get; set; } = string.Empty;

    // Set while the page shows what the file says: not a change to save
    private bool m_refreshing;

    partial void OnPlaybackVideoIndexChanged(int value) => SavePlaybackChoices();

    partial void OnPlaybackTranscodeIndexChanged(int value) => SavePlaybackChoices();

    /// <summary>Store the playback page's answers in the configuration ("ask" as no value). Automation and --output-root runs never do.</summary>
    private void SavePlaybackChoices()
    {
      if (m_refreshing || Program.Automation || Program.OutputRoot != null)
        return;
      try
      {
        var video = (PlaybackVideoChoice)Math.Clamp(PlaybackVideoIndex, 0, 2);
        var transcode = (PlaybackTranscodeChoice)Math.Clamp(PlaybackTranscodeIndex, 0, 2);
        (
          MainWindowViewModel.SafeLoadConfig() with
          {
            PlaybackVideo = video == PlaybackVideoChoice.Ask ? null : video,
            PlaybackTranscode = transcode == PlaybackTranscodeChoice.Ask ? null : transcode,
          }
        ).Save();
        PlaybackStatusText = string.Empty;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        PlaybackStatusText = "Could not save the configuration file: " + ex.Message;
      }
    }

    public string ConfigPath => FramePacingConfig.ResolvePath();

    public string GuiSettingsPath => GuiSettings.FilePath;

    /// <summary>The folder of the GUI's log files (<see cref="GuiLogging"/>).</summary>
    public string LogDirectory => GuiLogging.Directory;

    [ObservableProperty]
    public partial string LogStatusText { get; set; } = string.Empty;

    public string Version => MainWindowViewModel.Version;

    /// <summary>Read the configuration file again (after the setup dialog saved it).</summary>
    public void Refresh()
    {
      var config = MainWindowViewModel.SafeLoadConfig();
      FfmpegPathText = config.FfmpegPath ?? "not set: found on the PATH or in the usual install folders";
      CaptureDirectoryText = config.CaptureDirectory ?? MainWindowViewModel.DefaultCaptureDirectory;
      m_refreshing = true;
      PlaybackVideoIndex = (int)(config.PlaybackVideo ?? PlaybackVideoChoice.Ask);
      PlaybackTranscodeIndex = (int)(config.PlaybackTranscode ?? PlaybackTranscodeChoice.Ask);
      m_refreshing = false;
    }

    [RelayCommand]
    private async Task SetUpAsync()
    {
      await m_setUp();
      Refresh();
    }

    [RelayCommand]
    private void OpenConfigFile()
    {
      try
      {
        m_dialogs.OpenFile(FramePacingConfig.EnsureExists());
        StatusText = string.Empty;
      }
      catch (Exception ex)
      {
        StatusText = "Could not open the configuration file: " + ex.Message;
      }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
      try
      {
        Directory.CreateDirectory(GuiLogging.Directory);
        m_dialogs.ShowInFileManager(GuiLogging.Directory);
        LogStatusText = string.Empty;
      }
      catch (Exception ex)
      {
        LogStatusText = "Could not open the log folder: " + ex.Message;
      }
    }

    private void OnCapturePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(CaptureViewModel.ExperimentalFeatures))
        OnPropertyChanged(nameof(ExperimentalFeatures));
      else if (e.PropertyName is nameof(CaptureViewModel.FfmpegSummary) or nameof(CaptureViewModel.FfmpegReady))
      {
        OnPropertyChanged(nameof(FfmpegSummary));
        OnPropertyChanged(nameof(FfmpegReady));
      }
    }
  }
}
