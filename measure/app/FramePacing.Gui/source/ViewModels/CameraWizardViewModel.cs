//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The camera rig wizard (VERY EXPERIMENTAL camera support). A saved camera only needs its source and a quick check (Choose -> Source ->
//* Verify -> Done); a new camera is mounted, calibrated and saved under a name (Choose -> Mount -> Source -> Calibrate -> Save -> Done), so the
//* next time it can be picked from the list and calibration is skipped.
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
  public sealed partial class CameraWizardViewModel : ObservableObject
  {
    private readonly IDialogService m_dialogs;
    private readonly string m_libraryDirectory;
    private readonly Func<CameraSourceChoice, CancellationToken, ICaptureSource> m_openSource;
    private CancellationTokenSource? m_cancel;

    /// <param name="libraryDirectory">Where saved cameras live (<see cref="CameraRigLibrary"/>).</param>
    /// <param name="sources">The capture page's sources; the synthetic test game is left out (it has no camera).</param>
    /// <param name="initial">The capture page's current source, pre-selected when it can film.</param>
    /// <param name="openSource">Opens a source as whole camera frames.</param>
    public CameraWizardViewModel(
      IDialogService dialogs,
      string libraryDirectory,
      IEnumerable<DeviceItem> sources,
      CameraSourceChoice initial,
      Func<CameraSourceChoice, CancellationToken, ICaptureSource> openSource
    )
    {
      m_dialogs = dialogs;
      m_libraryDirectory = libraryDirectory;
      m_openSource = openSource;
      foreach (var source in sources.Where(s => s.Kind != SourceKind.Synthetic))
        Sources.Add(source);
      SelectedSource =
        Sources.FirstOrDefault(s => s == initial.Source) ?? Sources.FirstOrDefault(s => s.Kind == SourceKind.Device) ?? Sources.FirstOrDefault();
      MediaPath = initial.MediaPath;
      ModeText = initial.ModeText;
      InputFormat = initial.InputFormat;
      RecordedFpsText = initial.RecordedFps is { } fps ? fps.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
      RigName = $"camera-{DateTime.Now:yyyyMMdd}";
      ReloadSavedRigs();
      IsNewCamera = SavedRigs.Count == 0;
    }

    public event Action<bool>? CloseRequested;

    public string ExperimentalText => CameraRig.ExperimentalNotice;

    /// <summary>Set when the wizard finished: the camera to capture with and its source.</summary>
    public CameraWizardResult? Result { get; private set; }

    // ---- Navigation ----------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChooseStep))]
    [NotifyPropertyChangedFor(nameof(IsMountStep))]
    [NotifyPropertyChangedFor(nameof(IsSourceStep))]
    [NotifyPropertyChangedFor(nameof(IsCalibrateStep))]
    [NotifyPropertyChangedFor(nameof(IsSaveStep))]
    [NotifyPropertyChangedFor(nameof(IsVerifyStep))]
    [NotifyPropertyChangedFor(nameof(IsDoneStep))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(NextText))]
    [NotifyPropertyChangedFor(nameof(DoneText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial CameraWizardStep Step { get; set; } = CameraWizardStep.Choose;

    public bool IsChooseStep => Step == CameraWizardStep.Choose;
    public bool IsMountStep => Step == CameraWizardStep.Mount;
    public bool IsSourceStep => Step == CameraWizardStep.Source;
    public bool IsCalibrateStep => Step == CameraWizardStep.Calibrate;
    public bool IsSaveStep => Step == CameraWizardStep.Save;
    public bool IsVerifyStep => Step == CameraWizardStep.Verify;
    public bool IsDoneStep => Step == CameraWizardStep.Done;

    private CameraWizardStep[] Flow =>
      IsNewCamera
        ? new[]
        {
          CameraWizardStep.Choose,
          CameraWizardStep.Mount,
          CameraWizardStep.Source,
          CameraWizardStep.Calibrate,
          CameraWizardStep.Save,
          CameraWizardStep.Done,
        }
        : new[] { CameraWizardStep.Choose, CameraWizardStep.Source, CameraWizardStep.Verify, CameraWizardStep.Done };

    public string StepText => $"Step {Array.IndexOf(Flow, Step) + 1} of {Flow.Length}";

    public string StepTitle =>
      Step switch
      {
        CameraWizardStep.Choose => "Which camera?",
        CameraWizardStep.Mount => "Mount the camera and show the markers",
        CameraWizardStep.Source => "What does the camera deliver?",
        CameraWizardStep.Calibrate => "Calibrate",
        CameraWizardStep.Save => "Save the camera",
        CameraWizardStep.Verify => "Check the camera has not moved",
        _ => "Ready to capture",
      };

    public string NextText => Step == CameraWizardStep.Done ? "Finish" : "Next";

    public string DoneText =>
      $"Captures will use the camera '{(IsNewCamera ? RigName.Trim() : SelectedSavedRig?.Name)}' filming {SelectedSource?.Title}. Every capture "
      + "checks first that the camera has not moved, then stores only the two straightened marker zones. Press Finish, then Start capture.";

    private bool CanBack() => Step != CameraWizardStep.Choose && Step != CameraWizardStep.Done && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
      var flow = Flow;
      Step = flow[Math.Max(0, Array.IndexOf(flow, Step) - 1)];
    }

    private bool CanNext() =>
      !IsBusy
      && Step switch
      {
        CameraWizardStep.Choose => IsNewCamera || SelectedSavedRig?.Rig != null,
        CameraWizardStep.Source => SelectedSource != null && (!IsMediaSource || !string.IsNullOrWhiteSpace(MediaPath)),
        CameraWizardStep.Calibrate => Rig is { HasFailures: false },
        CameraWizardStep.Save => CameraRigLibrary.IsValidName(RigName),
        CameraWizardStep.Verify => !Checks.Any(c => c.IsFail),
        _ => true,
      };

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next()
    {
      try
      {
        ErrorText = string.Empty;
        if (Step == CameraWizardStep.Save)
          SaveRig();
        if (Step == CameraWizardStep.Source)
          _ = CurrentChoice(); // validates the recorded fps before moving on
        if (Step == CameraWizardStep.Done)
        {
          Result = new CameraWizardResult(IsNewCamera ? RigName.Trim() : SelectedSavedRig!.Name, CurrentChoice());
          CloseRequested?.Invoke(true);
          return;
        }
        var flow = Flow;
        Step = flow[Array.IndexOf(flow, Step) + 1];
      }
      catch (Exception ex)
      {
        ErrorText = ex.Message;
      }
    }

    [RelayCommand]
    private void Cancel()
    {
      m_cancel?.Cancel();
      CloseRequested?.Invoke(false);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CalibrateCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    // ---- Choose ----------------------------------------------------------------------------------------------------------------------------

    public ObservableCollection<SavedCameraRig> SavedRigs { get; } = new ObservableCollection<SavedCameraRig>();

    public bool HasSavedRigs => SavedRigs.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSavedCamera))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool IsNewCamera { get; set; }

    public bool IsSavedCamera
    {
      get => !IsNewCamera;
      set => IsNewCamera = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedRigSummary))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSavedRigCommand))]
    public partial SavedCameraRig? SelectedSavedRig { get; set; }

    public string SavedRigSummary => SelectedSavedRig is { } saved ? Describe(saved) : string.Empty;

    partial void OnSelectedSavedRigChanged(SavedCameraRig? value)
    {
      if (value != null)
        IsNewCamera = false;
    }

    private bool CanDeleteSavedRig() => SelectedSavedRig != null;

    [RelayCommand(CanExecute = nameof(CanDeleteSavedRig))]
    private void DeleteSavedRig()
    {
      if (SelectedSavedRig is not { } saved)
        return;
      try
      {
        CameraRigLibrary.Delete(saved.Name, m_libraryDirectory);
      }
      catch (Exception ex)
      {
        ErrorText = ex.Message;
      }
      ReloadSavedRigs();
      if (SavedRigs.Count == 0)
        IsNewCamera = true;
    }

    private void ReloadSavedRigs()
    {
      SavedRigs.Clear();
      foreach (var saved in CameraRigLibrary.List(m_libraryDirectory))
        SavedRigs.Add(saved);
      SelectedSavedRig = SavedRigs.FirstOrDefault(r => r.Rig != null);
      OnPropertyChanged(nameof(HasSavedRigs));
    }

    internal static string Describe(SavedCameraRig saved)
    {
      if (saved.Rig is not { } rig)
        return "Unreadable: " + saved.Error;
      int warnings = rig.Checks.Count(c => c.Level != CameraCheckLevel.Pass);
      var culture = CultureInfo.InvariantCulture;
      return string.Create(culture, $"{rig.CameraWidth}x{rig.CameraHeight} at {rig.CameraFps:0.#} fps, calibrated ")
        + rig.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", culture)
        + (rig.ScanoutDelayMs is { } delay ? string.Create(culture, $", scanout between the markers {delay:0.00} ms") : string.Empty)
        + (warnings > 0 ? $", {warnings} warning(s)" : ", all checks passed")
        + (string.IsNullOrEmpty(rig.Source) ? "." : $". Calibrated from {rig.Source}.");
    }

    // ---- Mount -----------------------------------------------------------------------------------------------------------------------------

    public static IReadOnlyList<string> MountChecklist { get; } =
      new[]
      {
        "The camera is on a tripod or arm at a fixed distance, roughly square to the screen. Nothing may move after calibration.",
        "The application draws the same marker in the TopLeft and BottomLeft slots (Unity: Tearing markers), with vsync on.",
        "The camera sees both markers sharply, at 3 or more camera pixels per module (zoom in or draw a larger marker).",
        "Focus and exposure are fixed (no auto modes). A short exposure, half the frame time or less, reduces blending.",
        "The display runs at full brightness without strobing or PWM dimming.",
        "The camera films at least twice the refresh rate, ideally 500-1000 fps.",
      };

    // ---- Source ----------------------------------------------------------------------------------------------------------------------------

    public ObservableCollection<DeviceItem> Sources { get; } = new ObservableCollection<DeviceItem>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMediaSource))]
    [NotifyPropertyChangedFor(nameof(IsDeviceSource))]
    [NotifyPropertyChangedFor(nameof(IsVideoFile))]
    [NotifyPropertyChangedFor(nameof(SourceHint))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial DeviceItem? SelectedSource { get; set; }

    public bool IsMediaSource => SelectedSource?.Kind is SourceKind.VideoFile or SourceKind.ImageFolder or SourceKind.Stream;

    public bool IsDeviceSource => SelectedSource?.Kind == SourceKind.Device;

    public bool IsVideoFile => SelectedSource?.Kind == SourceKind.VideoFile;

    public string SourceHint =>
      SelectedSource?.Kind switch
      {
        SourceKind.Device => "A live camera (UVC). High frame rate modes are usually MJPEG at a low resolution; the rig remembers the mode.",
        SourceKind.VideoFile => "A clip filmed with the mounted camera. Slow motion clips are stored at a playback rate: enter the recorded fps.",
        SourceKind.ImageFolder => "Frames exported by the camera, with the frame rate or a timestamp file set on the capture page.",
        SourceKind.Stream => "A live stream from the camera.",
        SourceKind.SyntheticCamera => "A simulated 1000 fps camera filming the synthetic game at an angle: try the whole flow without hardware.",
        _ => string.Empty,
      };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string MediaPath { get; set; }

    [ObservableProperty]
    public partial string ModeText { get; set; }

    [ObservableProperty]
    public partial string InputFormat { get; set; }

    [ObservableProperty]
    public partial string RecordedFpsText { get; set; }

    [RelayCommand]
    private async Task BrowseMediaAsync()
    {
      var path =
        SelectedSource?.Kind == SourceKind.ImageFolder
          ? await m_dialogs.PickFolderAsync("Select the folder with the camera's frames", MediaPath)
          : await m_dialogs.PickFileAsync("Select a clip filmed with the camera");
      if (path != null)
        MediaPath = path;
    }

    public CameraSourceChoice CurrentChoice()
    {
      double? recordedFps = null;
      if (IsVideoFile && !string.IsNullOrWhiteSpace(RecordedFpsText))
      {
        if (!double.TryParse(RecordedFpsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) || fps <= 0)
          throw new FormatException($"'{RecordedFpsText}' is not a frame rate");
        recordedFps = fps;
      }
      return new CameraSourceChoice(
        SelectedSource ?? throw new InvalidOperationException("Choose what the camera delivers"),
        MediaPath.Trim(),
        ModeText.Trim(),
        InputFormat.Trim(),
        recordedFps
      );
    }

    // ---- Calibrate / Verify ----------------------------------------------------------------------------------------------------------------

    public ObservableCollection<CameraCheckItem> Checks { get; } = new ObservableCollection<CameraCheckItem>();

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial CameraRig? Rig { get; set; }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CalibrateAsync()
    {
      Rig = null;
      await RunAsync(
        "Calibrating: finding both markers, fitting the camera geometry, measuring the scanout...",
        (source, token) =>
        {
          var rig = CameraCalibrator.Calibrate(source, new CameraCalibratorOptions(), token);
          return (rig.Checks, rig);
        },
        rig => Rig = rig is { HasFailures: false } ? rig with { Source = CurrentChoice().Source.Title } : null,
        rig =>
          rig is null ? "Calibration failed."
          : rig.HasFailures ? "Calibration failed: fix what the checks say and calibrate again."
          : rig.Checks.Any(c => c.Level == CameraCheckLevel.Warn) ? "Calibrated. Fix the warnings for reliable results, or continue."
          : "Calibrated: every check passed."
      );
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task VerifyAsync()
    {
      if (SelectedSavedRig?.Rig is not { } rig)
        return;
      await RunAsync(
        "Checking the camera against the saved calibration...",
        (source, token) => (CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), token), (CameraRig?)null),
        _ => { },
        _ =>
          Checks.Any(c => c.IsFail)
            ? "The camera or display moved: go back and set up the camera as new (calibrate again)."
            : "The camera has not moved."
      );
    }

    private async Task RunAsync(
      string busyText,
      Func<ICaptureSource, CancellationToken, (IReadOnlyList<CameraCheck> Checks, CameraRig? Rig)> work,
      Action<CameraRig?> apply,
      Func<CameraRig?, string> status
    )
    {
      IsBusy = true;
      ErrorText = string.Empty;
      Checks.Clear();
      StatusText = busyText;
      m_cancel = new CancellationTokenSource();
      CameraRig? rig = null;
      try
      {
        var choice = CurrentChoice();
        var token = m_cancel.Token;
        var (checks, result) = await Task.Run(() =>
        {
          using var source = m_openSource(choice, token);
          return work(source, token);
        });
        rig = result;
        foreach (var check in checks)
          Checks.Add(new CameraCheckItem(check));
        apply(rig);
        StatusText = status(rig);
      }
      catch (Exception ex)
      {
        StatusText = string.Empty;
        ErrorText = ex.Message;
      }
      finally
      {
        IsBusy = false;
        m_cancel.Dispose();
        m_cancel = null;
      }
    }

    // ---- Save ------------------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RigNameHint))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string RigName { get; set; }

    public string RigNameHint =>
      !CameraRigLibrary.IsValidName(RigName) ? "Use letters, digits, '-', '_', '.' and spaces."
      : CameraRigLibrary.Exists(RigName, m_libraryDirectory) ? "A camera with this name is already saved; saving replaces it."
      : "Next time, pick it from the list and skip calibration.";

    private void SaveRig()
    {
      var rig = Rig ?? throw new InvalidOperationException("Calibrate first");
      CameraRigLibrary.Save(rig, RigName.Trim(), m_libraryDirectory);
    }
  }
}
