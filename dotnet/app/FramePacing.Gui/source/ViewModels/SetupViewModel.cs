//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The setup dialog: get ffmpeg, point the tool at it, choose where captures go. Shown automatically when ffmpeg is missing and from the
//* Settings button. Everything is stored in mb-framepacing.json, so the command line tool uses the same settings.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class SetupViewModel : ObservableObject
  {
    private readonly IDialogService m_dialogs;
    private readonly FramePacingConfig m_config;

    /// <param name="firstRun">True when shown because ffmpeg is missing: the way out is "skip" rather than "cancel".</param>
    public SetupViewModel(IDialogService dialogs, FramePacingConfig config, string defaultCaptureDirectory, bool firstRun)
    {
      m_dialogs = dialogs;
      SkipText = firstRun ? "Skip (test game only)" : "Cancel";
      m_config = config;
      CaptureDirectory = config.CaptureDirectory ?? defaultCaptureDirectory;
      _ = CheckAsync(config.FfmpegPath ?? FfmpegLocator.TryFindInstalled());
    }

    /// <summary>Raised when the dialog should close; true = settings saved.</summary>
    public event Action<bool>? CloseRequested;

    public string InstallCommand => FfmpegLocator.InstallCommand;

    public string SkipText { get; }

    public string ConfigPath => FramePacingConfig.ResolvePath();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DoneCommand))]
    public partial string? FfmpegPath { get; set; }

    [ObservableProperty]
    public partial string FfmpegStatus { get; set; } = "Looking for ffmpeg...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DoneCommand))]
    public partial bool FfmpegOk { get; set; }

    [ObservableProperty]
    public partial bool FfmpegProblem { get; set; }

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    public partial string CaptureDirectory { get; set; }

    [RelayCommand]
    private void Download() => m_dialogs.OpenUrl(FfmpegLocator.DownloadPage);

    [RelayCommand]
    private Task CopyInstallCommandAsync() => m_dialogs.CopyToClipboardAsync(InstallCommand);

    [RelayCommand]
    private Task FindAutomaticallyAsync() => CheckAsync(FfmpegLocator.TryFindInstalled(), reportMissing: true);

    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
      var path = await m_dialogs.PickFileAsync(OperatingSystem.IsWindows() ? "Select ffmpeg.exe" : "Select the ffmpeg executable");
      if (path != null)
        await CheckAsync(path, reportMissing: true);
    }

    [RelayCommand]
    private async Task BrowseCaptureDirectoryAsync()
    {
      var path = await m_dialogs.PickFolderAsync("Where should captures be saved?", CaptureDirectory);
      if (path != null)
        CaptureDirectory = path;
    }

    [RelayCommand]
    private void OpenConfigFile()
    {
      try
      {
        m_dialogs.OpenFile(FramePacingConfig.EnsureExists());
      }
      catch (Exception ex)
      {
        FfmpegStatus = "Could not open the configuration file: " + ex.Message;
      }
    }

    private bool CanFinish() => FfmpegOk;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Done()
    {
      if (!Program.Demo && Program.OutputRoot == null)
      {
        try
        {
          (m_config with { FfmpegPath = FfmpegPath, CaptureDirectory = CaptureDirectory }).Save();
        }
        catch (Exception ex)
        {
          FfmpegStatus = "Could not save the settings: " + ex.Message;
          FfmpegProblem = true;
          return;
        }
      }
      CloseRequested?.Invoke(true);
    }

    /// <summary>Close without ffmpeg: only the synthetic test game can be captured, analysis works as usual.</summary>
    [RelayCommand]
    private void Skip() => CloseRequested?.Invoke(false);

    /// <summary>Run 'ffmpeg -version' on the candidate and report what was found.</summary>
    private async Task CheckAsync(string? path, bool reportMissing = false)
    {
      FfmpegOk = false;
      FfmpegProblem = false;
      if (string.IsNullOrWhiteSpace(path))
      {
        FfmpegPath = null;
        FfmpegStatus = reportMissing ? "ffmpeg was not found on this computer. Install it (step 1), then try again." : "ffmpeg is not set up yet.";
        FfmpegProblem = reportMissing;
        return;
      }

      IsChecking = true;
      FfmpegStatus = "Checking " + path;
      try
      {
        var versionLine = await Task.Run(() =>
          File.Exists(path) ? FfmpegDevices.GetVersion(path) : throw new FileNotFoundException("The file does not exist")
        );
        if (!versionLine.StartsWith("ffmpeg version", StringComparison.Ordinal))
          throw new InvalidDataException("This does not look like ffmpeg");
        var version = FfmpegDevices.ParseVersion(versionLine);
        FfmpegPath = path;
        if (version != null && version < FfmpegDevices.MinimumVersion)
        {
          FfmpegStatus = $"ffmpeg {version} is too old, version {FfmpegDevices.MinimumVersion} or newer is needed.";
          FfmpegProblem = true;
          return;
        }
        FfmpegStatus = $"✓ Found {Shorten(versionLine)}";
        FfmpegOk = true;
      }
      catch (Exception ex)
      {
        FfmpegPath = path;
        FfmpegStatus = $"'{path}' can not be used: {ex.Message}";
        FfmpegProblem = true;
      }
      finally
      {
        IsChecking = false;
      }
    }

    private static string Shorten(string versionLine)
    {
      // "ffmpeg version 9.0.2-essentials_build-www.gyan.dev Copyright (c) ..." -> "ffmpeg 9.0.2-essentials_build-www.gyan.dev"
      var parts = versionLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      return parts.Length >= 3 ? $"ffmpeg {parts[2]}" : versionLine;
    }
  }
}
