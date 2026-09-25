//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for the synthetic high speed camera: the screen it films (size, marker zones), where the camera sits (perspective, lens), and
//* what the display and sensor do to the image (scanout, panel response, exposure, blur, noise, clock drift).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed record SyntheticCameraOptions
  {
    /// <summary>Screen size in screen pixels. The markers are drawn in the TopLeft and BottomLeft slots.</summary>
    public int ScreenWidth { get; init; } = 640;
    public int ScreenHeight { get; init; } = 480;

    /// <summary>Marker module size in screen pixels.</summary>
    public int ModuleSizePx { get; init; } = 4;

    /// <summary>Distance of the marker slots from the screen edges in screen pixels.</summary>
    public int InsetPx { get; init; } = 16;

    /// <summary>Also draw the MiddleLeft tearing marker (the Unity overlay's tearing markers draw all three slots).</summary>
    public bool MiddleMarker { get; init; }

    /// <summary>Stored camera frame size.</summary>
    public int CameraWidth { get; init; } = 360;
    public int CameraHeight { get; init; } = 480;

    /// <summary>Screen to camera transform; null films the left part of the screen with some keystone and rotation.</summary>
    public Homography? ScreenToCamera { get; init; }

    /// <summary>Radial lens distortion (positive = barrel), relative to the half diagonal of the camera frame.</summary>
    public double LensDistortion { get; init; }

    /// <summary>Part of the refresh interval the scanout takes from the top row to the bottom row (the rest is vertical blanking).</summary>
    public double ScanoutFraction { get; init; } = 0.92;

    /// <summary>Panel response time constant: a pixel moves 63% of the way to its new value in this time after the scanout reaches it.</summary>
    public double PanelResponseSeconds { get; init; } = 0.0008;

    /// <summary>Exposure time as a fraction of the camera frame period (a global shutter).</summary>
    public double ExposureFraction { get; init; } = 0.9;

    /// <summary>Gaussian blur of the lens / focus in camera pixels (0 = sharp).</summary>
    public double BlurSigma { get; init; } = 0.6;

    /// <summary>Sensor noise standard deviation in luma steps.</summary>
    public double NoiseSigma { get; init; } = 2;

    /// <summary>The camera clock runs this many parts per million fast against the display clock.</summary>
    public double ClockDriftPpm { get; init; } = 40;

    /// <summary>Camera luma of the screen's black, white and scene background, and of whatever surrounds the screen.</summary>
    public byte ScreenBlack { get; init; } = 14;
    public byte ScreenWhite { get; init; } = 225;
    public byte SceneLuma { get; init; } = 96;
    public byte SurroundLuma { get; init; } = 24;

    /// <summary>Samples per camera pixel: spatial (per axis) and within the exposure.</summary>
    public int SpatialSamples { get; init; } = 2;
    public int TimeSamples { get; init; } = 4;

    public int Seed { get; init; } = 1234;
  }
}
