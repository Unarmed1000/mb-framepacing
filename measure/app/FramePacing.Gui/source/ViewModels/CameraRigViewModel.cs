//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The capture page's camera rig card (VERY EXPERIMENTAL camera support): a guided setup for filming the screen with a high speed camera on a
//* fixed mount. Calibrate from the selected source (a live camera, a clip or the synthetic camera), keep the rig file, verify it before every
//* capture.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class CameraRigViewModel : ObservableObject
  {
    public const string ExperimentalText = CameraRig.ExperimentalNotice;

    public const string SetupSteps =
      "1. Mount: put the camera on a tripod or arm at a fixed distance, roughly square to the screen, and do not touch it afterwards. Fix focus "
      + "and exposure (no auto modes). Set the display to full brightness without strobing.\n"
      + "2. Markers: the application draws the same marker in the TopLeft and BottomLeft slots, with vsync on. The camera must see both, at "
      + "3 or more camera pixels per module.\n"
      + "3. Calibrate: choose the camera (or a clip filmed with it) as the source and press Calibrate. Fix every warning and calibrate again.\n"
      + "4. Capture: with 'Film the screen' on, every capture verifies the rig first and stores only the two straightened marker zones.";

    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;
    private readonly Func<string> m_rigDirectory;
    private readonly Func<CancellationToken, ICaptureSource> m_createCameraSource;

    /// <param name="rigDirectory">Where calibrated rig files are written.</param>
    /// <param name="createCameraSource">Opens the selected source as whole camera frames (no region, no rectification).</param>
    public CameraRigViewModel(
      IDialogService dialogs,
      GuiSettings settings,
      Func<string> rigDirectory,
      Func<CancellationToken, ICaptureSource> createCameraSource
    )
    {
      m_dialogs = dialogs;
      m_settings = settings;
      m_rigDirectory = rigDirectory;
      m_createCameraSource = createCameraSource;
      UseCamera = settings.UseCamera;
      RigPath = settings.CameraRig ?? string.Empty;
      RecordedFpsText = settings.CameraRecordedFps ?? string.Empty;
    }

    public ObservableCollection<CameraCheckItem> Checks { get; } = new ObservableCollection<CameraCheckItem>();

    [ObservableProperty]
    public partial bool UseCamera { get; set; }

    [ObservableProperty]
    public partial string RigPath { get; set; }

    /// <summary>Video files: the rate a slow motion clip was really recorded at (empty = use the file's timestamps).</summary>
    [ObservableProperty]
    public partial string RecordedFpsText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CalibrateCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial bool IsBusy { get; set; }

    public double? RecordedFps
    {
      get
      {
        if (string.IsNullOrWhiteSpace(RecordedFpsText))
          return null;
        if (!double.TryParse(RecordedFpsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) || fps <= 0)
          throw new FormatException($"'{RecordedFpsText}' is not a frame rate");
        return fps;
      }
    }

    /// <summary>The rig file to capture with; throws with a hint when there is none yet.</summary>
    public CameraRig LoadRig()
    {
      if (string.IsNullOrWhiteSpace(RigPath))
        throw new InvalidOperationException("No camera rig yet: press Calibrate in the Camera rig card first.");
      return CameraRig.Load(RigPath.Trim());
    }

    /// <summary>Show checks (called from any thread by the capture before it starts).</summary>
    public void ShowChecks(IEnumerable<CameraCheck> checks, string status)
    {
      Checks.Clear();
      foreach (var check in checks)
        Checks.Add(new CameraCheckItem(check));
      StatusText = status;
    }

    [RelayCommand]
    private async Task BrowseRigAsync()
    {
      var path = await m_dialogs.PickFileAsync($"Select a camera rig file (*{CameraRig.FileExtension})");
      if (path != null)
        RigPath = path;
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CalibrateAsync()
    {
      IsBusy = true;
      ShowChecks(Array.Empty<CameraCheck>(), "Calibrating: finding both markers, measuring the scanout...");
      try
      {
        var rig = await Task.Run(() =>
        {
          using var source = m_createCameraSource(CancellationToken.None);
          return CameraCalibrator.Calibrate(source, new CameraCalibratorOptions(), CancellationToken.None);
        });
        if (rig.HasFailures)
        {
          ShowChecks(rig.Checks, "Calibration failed: fix the setup and calibrate again. No rig file was written.");
          return;
        }
        var directory = m_rigDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"rig-{DateTime.Now:yyyyMMdd-HHmmss}{CameraRig.FileExtension}");
        rig.Save(path);
        RigPath = path;
        UseCamera = true;
        string warnings = rig.Checks.Any(c => c.Level == CameraCheckLevel.Warn) ? " Fix the warnings for reliable results." : string.Empty;
        ShowChecks(rig.Checks, $"Calibrated and saved as {Path.GetFileName(path)}.{warnings}");
      }
      catch (Exception ex)
      {
        ShowChecks(Array.Empty<CameraCheck>(), "Calibration failed: " + ex.Message);
      }
      finally
      {
        IsBusy = false;
      }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task VerifyAsync()
    {
      IsBusy = true;
      ShowChecks(Array.Empty<CameraCheck>(), "Verifying the rig...");
      try
      {
        var rig = LoadRig();
        var checks = await Task.Run(() =>
        {
          using var source = m_createCameraSource(CancellationToken.None);
          return CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), CancellationToken.None);
        });
        bool failed = checks.Any(c => c.Level == CameraCheckLevel.Fail);
        ShowChecks(checks, failed ? "The camera or display moved: calibrate again." : "The rig still matches.");
      }
      catch (Exception ex)
      {
        ShowChecks(Array.Empty<CameraCheck>(), "Verification failed: " + ex.Message);
      }
      finally
      {
        IsBusy = false;
      }
    }

    public void StoreSettings()
    {
      m_settings.UseCamera = UseCamera;
      m_settings.CameraRig = RigPath;
      m_settings.CameraRecordedFps = RecordedFpsText;
    }
  }
}
