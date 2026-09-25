//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The capture page's camera card (VERY EXPERIMENTAL camera support): film the screen with a saved, calibrated camera. New cameras are set up
//* in the camera wizard; saved ones are picked from the list, so calibration is skipped and every capture only verifies the camera.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

    private readonly GuiSettings m_settings;
    private readonly Func<string> m_libraryDirectory;
    private readonly Func<Task> m_openWizard;
    private readonly Func<CancellationToken, ICaptureSource> m_openCameraSource;

    /// <param name="libraryDirectory">Where saved cameras live.</param>
    /// <param name="openWizard">Opens the camera wizard (it updates this card when it finishes).</param>
    /// <param name="openCameraSource">Opens the capture page's source as whole camera frames.</param>
    public CameraRigViewModel(
      GuiSettings settings,
      Func<string> libraryDirectory,
      Func<Task> openWizard,
      Func<CancellationToken, ICaptureSource> openCameraSource
    )
    {
      m_settings = settings;
      m_libraryDirectory = libraryDirectory;
      m_openWizard = openWizard;
      m_openCameraSource = openCameraSource;
      UseCamera = settings.UseCamera;
      RecordedFpsText = settings.CameraRecordedFps ?? string.Empty;
      ReloadLibrary(settings.CameraRig);
    }

    public ObservableCollection<SavedCameraRig> SavedRigs { get; } = new ObservableCollection<SavedCameraRig>();

    public bool HasSavedRigs => SavedRigs.Count > 0;

    [ObservableProperty]
    public partial bool UseCamera { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RigSummary))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial SavedCameraRig? SelectedRig { get; set; }

    public string RigSummary => SelectedRig is { } saved ? CameraWizardViewModel.Describe(saved) : "No camera saved yet: set one up.";

    /// <summary>Video files: the rate a slow motion clip was really recorded at (empty = use the file's timestamps).</summary>
    [ObservableProperty]
    public partial string RecordedFpsText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetUpCommand))]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<CameraCheckItem> Checks { get; } = new ObservableCollection<CameraCheckItem>();

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

    /// <summary>Read the saved cameras again and select <paramref name="name"/> (or keep the selection).</summary>
    public void ReloadLibrary(string? name = null)
    {
      name ??= SelectedRig?.Name;
      SavedRigs.Clear();
      foreach (var saved in CameraRigLibrary.List(m_libraryDirectory()))
        SavedRigs.Add(saved);
      SelectedRig = SavedRigs.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) ?? SavedRigs.FirstOrDefault();
      OnPropertyChanged(nameof(HasSavedRigs));
    }

    /// <summary>The camera to capture with; throws with a hint when there is none.</summary>
    public CameraRig LoadRig()
    {
      if (SelectedRig is not { } saved)
        throw new InvalidOperationException("No camera is set up yet: press 'Set up camera...' in the Camera card.");
      return saved.Rig ?? throw new InvalidOperationException($"The saved camera '{saved.Name}' can not be read: {saved.Error}");
    }

    /// <summary>Show checks (called by the capture before it starts).</summary>
    public void ShowChecks(IEnumerable<CameraCheck> checks, string status)
    {
      Checks.Clear();
      foreach (var check in checks)
        Checks.Add(new CameraCheckItem(check));
      StatusText = status;
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SetUpAsync() => m_openWizard();

    private bool CanVerify() => !IsBusy && SelectedRig?.Rig != null;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
      IsBusy = true;
      ShowChecks(Array.Empty<CameraCheck>(), "Checking the camera against its saved calibration...");
      try
      {
        var rig = LoadRig();
        var checks = await Task.Run(() =>
        {
          using var source = m_openCameraSource(CancellationToken.None);
          return CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), CancellationToken.None);
        });
        bool failed = checks.Any(c => c.Level == CameraCheckLevel.Fail);
        ShowChecks(checks, failed ? "The camera or display moved: set the camera up again." : "The camera has not moved.");
      }
      catch (Exception ex)
      {
        ShowChecks(Array.Empty<CameraCheck>(), "Check failed: " + ex.Message);
      }
      finally
      {
        IsBusy = false;
      }
    }

    public void StoreSettings()
    {
      m_settings.UseCamera = UseCamera;
      m_settings.CameraRig = SelectedRig?.Name;
      m_settings.CameraRecordedFps = RecordedFpsText;
    }
  }
}
