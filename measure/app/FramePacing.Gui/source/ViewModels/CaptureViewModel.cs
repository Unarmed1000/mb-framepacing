//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture page: pick a source, (optionally) tune the mode, start/stop, live status and preview. ffmpeg and the capture folder come from the
//* configuration file, which the setup dialog edits.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class CaptureViewModel : ObservableObject
  {
    private const string SyntheticTitle = "Synthetic test game";

    private static readonly DeviceItem[] g_otherSources =
    [
      new DeviceItem("Video file...", null, SourceKind.VideoFile),
      new DeviceItem("Image folder...", null, SourceKind.ImageFolder),
      new DeviceItem("Network stream (URL)...", null, SourceKind.Stream),
      new DeviceItem(SyntheticTitle, null, SourceKind.Synthetic),
      new DeviceItem("Synthetic camera (very experimental)", null, SourceKind.SyntheticCamera),
    ];

    private readonly IDialogService m_dialogs;
    private readonly GuiSettings m_settings;
    private FramePacingConfig m_config = new FramePacingConfig();
    private string? m_ffmpeg;
    private string m_ffmpegVersion = string.Empty;
    private CancellationTokenSource? m_cancel;
    private WriteableBitmap? m_previewBitmap;
    private long m_lastPreviewTicks;

    public CaptureViewModel(IDialogService dialogs, GuiSettings settings)
    {
      m_dialogs = dialogs;
      m_settings = settings;
      ModeText = settings.Mode ?? string.Empty;
      InputFormat = settings.InputFormat ?? string.Empty;
      ScaleText = settings.Scale ?? string.Empty;
      RoiText = settings.Roi ?? string.Empty;
      DurationText = settings.Duration ?? "30s";
      WaitForStart = settings.WaitForStart;
      StopAtEnd = settings.StopAtEnd;
      OutputRoot = DefaultOutputRoot();
      MediaPath = settings.MediaPath ?? string.Empty;
      ImageFpsText = settings.ImageFps ?? "240";
      TimestampFile = settings.TimestampFile ?? string.Empty;
      foreach (var item in g_otherSources)
        Devices.Add(item);
      SelectedDevice = Devices.First(d => d.Kind == SourceKind.Synthetic);
      Camera = new CameraRigViewModel(settings, CameraLibraryDirectory, OpenCameraWizardAsync, token => OpenCameraSource(CurrentChoice(), token));
    }

    /// <summary>The camera card (VERY EXPERIMENTAL).</summary>
    public CameraRigViewModel Camera { get; }

    public event Action<string>? CaptureCompleted;

    public ObservableCollection<DeviceItem> Devices { get; } = new ObservableCollection<DeviceItem>();

    public ObservableCollection<CaptureMode> Modes { get; } = new ObservableCollection<CaptureMode>();

    // ---- ffmpeg / configuration ---------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool FfmpegReady { get; set; }

    [ObservableProperty]
    public partial string FfmpegSummary { get; set; } = "Looking for ffmpeg...";

    [ObservableProperty]
    public partial string FfmpegToolTip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputRoot { get; set; }

    // ---- Source --------------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFfmpegDevice))]
    [NotifyPropertyChangedFor(nameof(IsMediaSource))]
    [NotifyPropertyChangedFor(nameof(IsImageFolder))]
    [NotifyPropertyChangedFor(nameof(CanBrowseMedia))]
    [NotifyPropertyChangedFor(nameof(MediaPathLabel))]
    [NotifyPropertyChangedFor(nameof(SourceHint))]
    public partial DeviceItem? SelectedDevice { get; set; }

    /// <summary>Video file, image folder or stream URL, depending on the selected source.</summary>
    [ObservableProperty]
    public partial string MediaPath { get; set; }

    [ObservableProperty]
    public partial string ImageFpsText { get; set; }

    [ObservableProperty]
    public partial string TimestampFile { get; set; } = string.Empty;

    public bool IsMediaSource => SelectedDevice?.Kind is SourceKind.VideoFile or SourceKind.ImageFolder or SourceKind.Stream;

    public bool IsImageFolder => SelectedDevice?.Kind == SourceKind.ImageFolder;

    public bool CanBrowseMedia => SelectedDevice?.Kind is SourceKind.VideoFile or SourceKind.ImageFolder;

    public string MediaPathLabel =>
      SelectedDevice?.Kind switch
      {
        SourceKind.VideoFile => "Video file",
        SourceKind.ImageFolder => "Folder with the images",
        _ => "Stream URL",
      };

    [ObservableProperty]
    public partial CaptureMode? SelectedMode { get; set; }

    [ObservableProperty]
    public partial string ModeText { get; set; }

    [ObservableProperty]
    public partial string InputFormat { get; set; }

    [ObservableProperty]
    public partial string ScaleText { get; set; }

    [ObservableProperty]
    public partial string RoiText { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public bool IsFfmpegDevice => SelectedDevice?.Kind == SourceKind.Device;

    public string SourceHint =>
      SelectedDevice?.Kind switch
      {
        SourceKind.Synthetic =>
          "A simulated 144 Hz game with stalls and skipped frames, captured at 500 fps. Use it to try the tool without hardware.",
        SourceKind.VideoFile =>
          "Any video ffmpeg can read (mp4, mkv, mov, ...), e.g. a lossless recording or a high speed camera clip. Its own timestamps are used.",
        SourceKind.ImageFolder =>
          "A folder of frames (png, jpg, bmp, ...), in name order at the given frame rate, or with exact times from a CSV (fileName,timeMs).",
        SourceKind.Stream => "A live stream ffmpeg can open: rtsp://, srt://, udp://, http(s)://. Stop it with Stop or a duration.",
        SourceKind.SyntheticCamera =>
          "VERY EXPERIMENTAL: the synthetic game (60 Hz) filmed by a simulated 1000 fps camera at an angle. Set it up with Set up camera... in the Camera card, then capture.",
        _ => "Records the capture card through ffmpeg. The defaults use the device's own mode; open Advanced to choose one.",
      };

    [RelayCommand]
    private async Task BrowseMediaAsync()
    {
      var path =
        SelectedDevice?.Kind == SourceKind.ImageFolder
          ? await m_dialogs.PickFolderAsync("Select the folder with the images", MediaPath)
          : await m_dialogs.PickFileAsync("Select a video file");
      if (path != null)
        MediaPath = path;
    }

    [RelayCommand]
    private async Task BrowseTimestampsAsync()
    {
      var path = await m_dialogs.PickFileAsync("Select a timestamp file (CSV: fileName,timeMs)");
      if (path != null)
        TimestampFile = path;
    }

    // ---- Recording -----------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string DurationText { get; set; }

    [ObservableProperty]
    public partial bool WaitForStart { get; set; }

    [ObservableProperty]
    public partial bool StopAtEnd { get; set; }

    // ---- Live status ---------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(LocateMarkerCommand))]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(LocateMarkerCommand))]
    public partial bool IsLocating { get; set; }

    /// <summary>What 'Locate marker' found.</summary>
    [ObservableProperty]
    public partial string LocateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PhaseText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "-";

    [ObservableProperty]
    public partial string FpsText { get; set; } = "-";

    [ObservableProperty]
    public partial string FramesText { get; set; } = "-";

    [ObservableProperty]
    public partial string BandwidthText { get; set; } = "-";

    [ObservableProperty]
    public partial string DropsText { get; set; } = "-";

    [ObservableProperty]
    public partial bool HasDrops { get; set; }

    [ObservableProperty]
    public partial double RingFillPercent { get; set; }

    [ObservableProperty]
    public partial string MarkerText { get; set; } = "No marker seen yet";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial WriteableBitmap? Preview { get; set; }

    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLastCaptureCommand))]
    public partial string LastCaptureDirectory { get; set; } = string.Empty;

    public bool IsIdle => !IsCapturing;

    partial void OnSelectedDeviceChanged(DeviceItem? value)
    {
      Modes.Clear();
      if (value?.Device is { } device && m_ffmpeg != null)
        _ = LoadModesAsync(m_ffmpeg, device);
    }

    partial void OnSelectedModeChanged(CaptureMode? value)
    {
      if (value == null)
        return;
      ModeText = value.Fps > 0 ? FormattableString.Invariant($"{value.Width}x{value.Height}@{value.Fps:0.###}") : $"{value.Width}x{value.Height}";
      InputFormat = value.Format;
    }

    /// <summary>Re-read the configuration, find ffmpeg and list the capture devices.</summary>
    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
      IsRefreshing = true;
      ErrorText = string.Empty;
      try
      {
        m_config = FramePacingConfig.Load();
      }
      catch (Exception ex)
      {
        m_config = new FramePacingConfig();
        ErrorText = ex.Message;
      }
      OutputRoot = DefaultOutputRoot();

      var previous = SelectedDevice?.Title ?? m_settings.LastDevice;
      try
      {
        var config = m_config;
        var (ffmpeg, version, devices) = await Task.Run(() =>
        {
          var path = FfmpegLocator.Find(null, config);
          return (path, FfmpegDevices.GetVersion(path), FfmpegDevices.ListDevices(path));
        });
        m_ffmpeg = ffmpeg;
        m_ffmpegVersion = version;
        FfmpegReady = true;
        FfmpegSummary = DescribeVersion(version);
        FfmpegToolTip = $"{version}{Environment.NewLine}{ffmpeg}";

        Devices.Clear();
        foreach (var device in devices)
          Devices.Add(new DeviceItem(device.Name, device));
        foreach (var item in g_otherSources)
          Devices.Add(item);
      }
      catch (Exception)
      {
        m_ffmpeg = null;
        FfmpegReady = false;
        FfmpegSummary = "ffmpeg not set up";
        FfmpegToolTip = "Open Settings to set up ffmpeg";
        Devices.Clear();
        foreach (var item in g_otherSources)
          Devices.Add(item);
      }
      finally
      {
        SelectedDevice = Devices.FirstOrDefault(d => d.Title == previous) ?? Devices[0];
        IsRefreshing = false;
      }
    }

    /// <summary>Capture the synthetic test game with its default settings (the --demo command line switch).</summary>
    public void StartDemo()
    {
      SelectedDevice = Devices.First(d => d.Kind == SourceKind.Synthetic);
      if (StartCommand.CanExecute(null))
        StartCommand.Execute(null);
    }

    private bool CanOpenLastCapture() => !string.IsNullOrEmpty(LastCaptureDirectory);

    [RelayCommand(CanExecute = nameof(CanOpenLastCapture))]
    private void OpenLastCapture() => m_dialogs.ShowInFileManager(LastCaptureDirectory);

    private bool CanStart() => !IsCapturing && !IsLocating;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
      ErrorText = string.Empty;
      HasDrops = false;
      MarkerText = "No marker seen yet";
      SaveSettings();

      var device = SelectedDevice;
      var prefix = device?.Kind is SourceKind.VideoFile or SourceKind.ImageFolder or SourceKind.Stream ? "import" : "capture";
      var directory = Path.Combine(OutputRoot, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}");
      IsCapturing = true;
      PhaseText = "Starting...";
      m_cancel = new CancellationTokenSource();
      var token = m_cancel.Token;
      try
      {
        var runOptions = new CaptureRunOptions
        {
          OutputDirectory = directory,
          Duration = DurationParser.ParseOptional(DurationText),
          WaitForStart = WaitForStart,
          StopAtEnd = StopAtEnd,
          ToolVersion = MainWindowViewModel.Version,
          Preview = OnPreview,
        };
        var result = await Task.Run(
          () =>
          {
            using var source = CreateSource(device, ref runOptions, token);
            return CaptureRunner.Run(source, runOptions, progress => Dispatcher.UIThread.Post(() => ShowProgress(progress)), token);
          },
          CancellationToken.None
        );
        LastCaptureDirectory = result.Directory;
        m_settings.LastCaptureDirectory = result.Directory;
        m_settings.Save();
        PhaseText = $"Saved {result.Session.FramesWritten} frames ({result.Session.StopReason})";
        CaptureCompleted?.Invoke(result.Directory);
      }
      catch (Exception ex)
      {
        PhaseText = "Capture failed";
        ErrorText = ex.Message;
      }
      finally
      {
        IsCapturing = false;
        m_cancel.Dispose();
        m_cancel = null;
      }
    }

    private bool CanStop() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => m_cancel?.Cancel();

    private ICaptureSource CreateSource(DeviceItem? device, ref CaptureRunOptions runOptions, CancellationToken cancellationToken)
    {
      if (device?.Kind == SourceKind.SyntheticCamera)
      {
        if (!Camera.UseCamera)
          throw new InvalidOperationException("The synthetic camera needs a camera: press Set up camera... in the Camera card first.");
        var syntheticRig = VerifyRig(new SyntheticCameraSource(CreateSyntheticCamera()), cancellationToken);
        runOptions = runOptions with { Camera = syntheticRig };
        return new RectifyingCaptureSource(new SyntheticCameraSource(CreateSyntheticCamera()), syntheticRig);
      }

      var ffmpegOptions = CreateFfmpegOptions(CurrentChoice() with { Source = device ?? Devices[0] }, runOptions.OutputDirectory);
      if (ffmpegOptions != null && Camera.UseCamera)
      {
        // EXPERIMENTAL camera capture: check the camera still sees the markers where it was calibrated, then store the rectified zones
        using (var check = FfmpegCaptureSource.Start(ffmpegOptions, TimeSpan.FromSeconds(30)))
          ffmpegOptions = ffmpegOptions with { Camera = VerifyRig(check, cancellationToken) };
        runOptions = runOptions with
        {
          Camera = ffmpegOptions.Camera,
          RecordedFps = ffmpegOptions.RecordedFps,
          FfmpegVersion = m_ffmpegVersion,
          FfmpegCommandLine = string.Join(" ", FfmpegCommandBuilder.BuildCapture(ffmpegOptions)),
        };
        return FfmpegCaptureSource.Start(ffmpegOptions, TimeSpan.FromSeconds(30));
      }
      if (ffmpegOptions != null)
      {
        ffmpegOptions = ApplyRegion(ffmpegOptions, cancellationToken);
        runOptions = runOptions with
        {
          FfmpegVersion = m_ffmpegVersion,
          FfmpegCommandLine = string.Join(" ", FfmpegCommandBuilder.BuildCapture(ffmpegOptions)),
        };
        return FfmpegCaptureSource.Start(ffmpegOptions, TimeSpan.FromSeconds(device?.Device != null ? 20 : 30));
      }

      // The synthetic game: a 144 Hz game with stalls and skipped frames, captured at 500 fps, with start/end markers
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 500,
          RefreshHz = 144,
          RunSeconds = 3,
          Width = 480,
          Height = 270,
          ModuleSizePx = 3,
          OriginX = 24,
          OriginY = 24,
          StallEvery = 37,
          SkipEvery = 53,
          RunName = "synthetic test game",
        }
      );
      return new SyntheticCaptureSource(scenario, paced: true);
    }

    /// <summary>Load the rig and check it against a few frames of <paramref name="source"/> (disposed); throws when the camera moved.</summary>
    private CameraRig VerifyRig(ICaptureSource source, CancellationToken cancellationToken)
    {
      using (source)
      {
        var rig = Camera.LoadRig();
        Dispatcher.UIThread.Post(() => PhaseText = "Verifying the camera rig...");
        var checks = CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), cancellationToken);
        bool failed = checks.Any(c => c.Level == CameraCheckLevel.Fail);
        Dispatcher.UIThread.Post(() => Camera.ShowChecks(checks, failed ? "The camera or display moved: calibrate again." : "Rig verified."));
        if (failed)
          throw new InvalidOperationException("The camera rig check failed: the camera or display moved. Calibrate again.");
        return rig;
      }
    }

    /// <summary>The capture page's source as the camera wizard and the camera card see it.</summary>
    public CameraSourceChoice CurrentChoice()
    {
      double? recordedFps;
      try
      {
        recordedFps = Camera?.RecordedFps;
      }
      catch (FormatException)
      {
        recordedFps = null;
      }
      return new CameraSourceChoice(SelectedDevice ?? Devices[0], MediaPath, ModeText, InputFormat, recordedFps);
    }

    /// <summary>A source as whole camera frames, for calibrating or verifying a camera rig.</summary>
    private ICaptureSource OpenCameraSource(CameraSourceChoice choice, CancellationToken cancellationToken)
    {
      if (choice.Source.Kind == SourceKind.SyntheticCamera)
        return new SyntheticCameraSource(CreateSyntheticCamera());
      var options =
        CreateFfmpegOptions(choice, Path.Combine(Path.GetTempPath(), "mb-framepacing-camera"))
        ?? throw new InvalidOperationException("Choose the camera (a capture device), a clip filmed with it or the synthetic camera as the source.");
      return FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30));
    }

    /// <summary>Saved cameras: the shared library, or a folder in the output root for demo and automation runs (never the user's library).</summary>
    private string CameraLibraryDirectory() =>
      Program.OutputRoot != null ? Path.Combine(Program.OutputRoot, CameraRigLibrary.DirectoryName)
      : Program.Demo ? Path.Combine(DefaultOutputRoot(), CameraRigLibrary.DirectoryName)
      : CameraRigLibrary.DefaultDirectory;

    /// <summary>Open the camera rig wizard; when it finishes the capture page films with the chosen camera.</summary>
    private async Task OpenCameraWizardAsync()
    {
      var wizard = CreateCameraWizard();
      if (await m_dialogs.ShowCameraWizardAsync(wizard) && wizard.Result is { } result)
        ApplyCameraWizardResult(result);
    }

    /// <summary>A camera rig wizard for the capture page's sources, starting from its current source.</summary>
    public CameraWizardViewModel CreateCameraWizard() =>
      new CameraWizardViewModel(m_dialogs, CameraLibraryDirectory(), Devices, CurrentChoice(), OpenCameraSource);

    /// <summary>Capture with the camera and source the wizard set up.</summary>
    public void ApplyCameraWizardResult(CameraWizardResult result)
    {
      var source = result.Source;
      SelectedDevice = Devices.FirstOrDefault(d => d.Title == source.Source.Title) ?? SelectedDevice;
      if (source.Source.Kind is SourceKind.VideoFile or SourceKind.ImageFolder or SourceKind.Stream)
        MediaPath = source.MediaPath;
      if (source.Source.Kind == SourceKind.Device)
      {
        ModeText = source.ModeText;
        InputFormat = source.InputFormat;
      }
      Camera.RecordedFpsText = source.RecordedFps is { } fps ? fps.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
      Camera.ReloadLibrary(result.RigName);
      Camera.UseCamera = true;
    }

    /// <summary>The synthetic camera: the 60 Hz synthetic game with stalls and skips, filmed at an angle by a simulated 1000 fps camera.</summary>
    private static SyntheticCamera CreateSyntheticCamera() =>
      new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = 1000,
            RefreshHz = 60,
            RunSeconds = 3,
            StallEvery = 37,
            SkipEvery = 53,
            RunName = "synthetic camera",
          }
        ),
        new SyntheticCameraOptions()
      );

    /// <summary>
    /// The ffmpeg options for the selected capture device, video file, image folder or stream, without crop and scale; null for the synthetic
    /// game. An image folder writes its ffconcat list into <paramref name="workDirectory"/>.
    /// </summary>
    private FfmpegCaptureOptions? CreateFfmpegOptions(CameraSourceChoice choice, string workDirectory)
    {
      var device = choice.Source;
      if (device.Kind is SourceKind.VideoFile or SourceKind.ImageFolder or SourceKind.Stream)
      {
        var ffmpegPath = m_ffmpeg ?? throw new InvalidOperationException("ffmpeg is not set up. Open Settings to set it up.");
        if (string.IsNullOrWhiteSpace(choice.MediaPath))
          throw new InvalidOperationException(
            $"Choose the {(device.Kind == SourceKind.ImageFolder ? "folder with the images" : device.Kind == SourceKind.VideoFile ? "video file" : "stream URL")} first."
          );
        double? fps = null;
        if (device.Kind == SourceKind.ImageFolder && string.IsNullOrWhiteSpace(TimestampFile))
        {
          if (!double.TryParse(ImageFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
            throw new FormatException($"'{ImageFpsText}' is not a frame rate");
          fps = parsed;
        }
        var media = MediaInput.Create(
          choice.MediaPath.Trim(),
          new MediaInputOptions
          {
            Fps = fps,
            TimestampFile = string.IsNullOrWhiteSpace(TimestampFile) ? null : TimestampFile.Trim(),
            RecordedFps = device.Kind == SourceKind.VideoFile ? choice.RecordedFps : null,
          },
          workDirectory
        );
        return media.ToCaptureOptions(ffmpegPath);
      }

      if (device.Device is not { } captureDevice)
        return null;
      return new FfmpegCaptureOptions
      {
        FfmpegPath = m_ffmpeg ?? throw new InvalidOperationException("ffmpeg is not set up. Open Settings to set it up."),
        Device = captureDevice,
        Mode = string.IsNullOrWhiteSpace(choice.ModeText) ? default : RequestedMode.Parse(choice.ModeText.Trim()),
        InputFormat = string.IsNullOrWhiteSpace(choice.InputFormat) ? null : choice.InputFormat.Trim(),
      };
    }

    /// <summary>The stored size and region; "auto" as the region finds the marker first and stores only its region (fast capture).</summary>
    private FfmpegCaptureOptions ApplyRegion(FfmpegCaptureOptions options, CancellationToken cancellationToken)
    {
      if (FfmpegMarkerLocator.IsAutoRoi(RoiText))
      {
        if (!string.IsNullOrWhiteSpace(ScaleText))
          throw new InvalidOperationException("The region 'auto' chooses the stored size itself; clear the stored size.");
        Dispatcher.UIThread.Post(() => PhaseText = "Looking for the marker...");
        return FfmpegMarkerLocator.Locate(options, FfmpegMarkerLocator.DefaultTimeout, cancellationToken).Apply(options);
      }
      return options with
      {
        Scale = string.IsNullOrWhiteSpace(ScaleText) ? null : RequestedMode.ParseSize(ScaleText.Trim(), "scale"),
        Roi = string.IsNullOrWhiteSpace(RoiText) ? null : PixelRect.Parse(RoiText.Trim()),
      };
    }

    /// <summary>Find the marker now and fill in the region and stored size, so the next captures store only the marker's region.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task LocateMarkerAsync()
    {
      ErrorText = string.Empty;
      IsLocating = true;
      LocateText = "Looking for the marker...";
      var choice = CurrentChoice();
      try
      {
        var workDirectory = Path.Combine(Path.GetTempPath(), "mb-framepacing-locate");
        var result = await Task.Run(() =>
        {
          var options =
            CreateFfmpegOptions(choice, workDirectory)
            ?? throw new InvalidOperationException("The synthetic sources need no region; choose a capture device or a file.");
          return FfmpegMarkerLocator.Locate(options, FfmpegMarkerLocator.DefaultTimeout, CancellationToken.None);
        });
        RoiText = result.Crop.Roi.ToString();
        ScaleText = result.Scale is { } scale ? $"{scale.Width}x{scale.Height}" : string.Empty;
        LocateText = result.Summary + " The marker must not move.";
      }
      catch (Exception ex)
      {
        LocateText = string.Empty;
        ErrorText = ex.Message;
      }
      finally
      {
        IsLocating = false;
      }
    }

    private async Task LoadModesAsync(string ffmpeg, CaptureDevice device)
    {
      try
      {
        var modes = await Task.Run(() => FfmpegDevices.ListModes(ffmpeg, device));
        if (SelectedDevice?.Device != device)
          return;
        foreach (var mode in modes.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Fps))
          Modes.Add(mode);
      }
      catch (Exception ex)
      {
        ErrorText = "Could not list the device's modes: " + ex.Message;
      }
    }

    /// <summary>Runs on the capture runner's thread: convert at most ~10 times a second, then hand the pixels to the UI thread.</summary>
    private void OnPreview(GrayImage frame, long captureIndex)
    {
      long now = Stopwatch.GetTimestamp();
      if (Stopwatch.GetElapsedTime(m_lastPreviewTicks, now) < TimeSpan.FromMilliseconds(100))
        return;
      m_lastPreviewTicks = now;
      var pixels = GrayBitmap.ToBgra(frame);
      int width = frame.Width;
      int height = frame.Height;
      Dispatcher.UIThread.Post(() =>
      {
        m_previewBitmap = GrayBitmap.Update(m_previewBitmap, pixels, width, height);
        // Force the Image control to redraw the reused bitmap
        Preview = null;
        Preview = m_previewBitmap;
        HasPreview = true;
      });
    }

    private void ShowProgress(CaptureProgress progress)
    {
      var r = progress.Recorder;
      double seconds = Math.Max(progress.Elapsed.TotalSeconds, 1e-6);
      PhaseText = progress.Phase switch
      {
        CapturePhase.WaitingForStart => "Waiting for the start marker...",
        CapturePhase.Recording => "Recording",
        CapturePhase.Stopping => "Stopping...",
        _ => "Finished",
      };
      ElapsedText = seconds.ToString("0.0 's'", CultureInfo.InvariantCulture);
      FpsText = (r.FramesCaptured / seconds).ToString("0", CultureInfo.InvariantCulture);
      FramesText = r.FramesWritten.ToString("N0", CultureInfo.InvariantCulture);
      BandwidthText = (r.BytesWritten / seconds / (1024 * 1024)).ToString("0 'MiB/s'", CultureInfo.InvariantCulture);
      long drops = r.FramesDropped + progress.SourceDroppedFrames;
      DropsText = drops.ToString(CultureInfo.InvariantCulture);
      HasDrops = drops > 0;
      RingFillPercent = r.RingCapacity > 0 ? 100.0 * r.RingFill / r.RingCapacity : 0;
      if (progress.LastMarker is { } marker)
        MarkerText =
          $"{marker.Payload.Kind} marker, run {marker.Payload.RunId}, frame {marker.Payload.FrameIndex}"
          + (marker.Start != null ? $"  '{marker.Start.Name}'" : string.Empty);
    }

    /// <summary>"ffmpeg 7.1" for release builds, "ffmpeg 2026-03-15" for dated snapshot builds.</summary>
    internal static string DescribeVersion(string versionLine)
    {
      if (FfmpegDevices.ParseVersion(versionLine) is { } release)
        return $"ffmpeg {release}";
      var parts = versionLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 3)
        return "ffmpeg ready";
      var token = parts[2];
      int git = token.IndexOf("-git", StringComparison.Ordinal);
      return "ffmpeg " + (git > 0 ? token[..git] : token.Split('-')[0]);
    }

    private string DefaultOutputRoot() =>
      Program.OutputRoot
      ?? m_config.CaptureDirectory
      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mb-framepacing");

    private void SaveSettings()
    {
      StoreSettings();
      m_settings.Save();
    }

    /// <summary>Copy the current options into the settings (saved when a capture starts and when the window closes).</summary>
    public void StoreSettings()
    {
      m_settings.LastDevice = SelectedDevice?.Title;
      m_settings.Mode = ModeText;
      m_settings.InputFormat = InputFormat;
      m_settings.Scale = ScaleText;
      m_settings.Roi = RoiText;
      m_settings.Duration = DurationText;
      m_settings.WaitForStart = WaitForStart;
      m_settings.StopAtEnd = StopAtEnd;
      m_settings.MediaPath = MediaPath;
      m_settings.ImageFps = ImageFpsText;
      m_settings.TimestampFile = TimestampFile;
      Camera.StoreSettings();
    }
  }
}
