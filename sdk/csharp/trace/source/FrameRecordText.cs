//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The strings of the frame record: the slice an application logs to a
//* trace for each frame, so that a trace and a capture of the same run can be merged. The document is the reference; the C++ module has
//* the same strings under the same names.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>The strings of the frame record.</summary>
  public static class FrameRecordText
  {
    /// <summary>The category the slices are logged in.</summary>
    public const string Category = "mbfp";

    /// <summary>The name of the track the slices are on, under the application's process.</summary>
    public const string TrackName = "mbfp frames";

    /// <summary>What a slice's name begins with: the run id and the frame index follow, as decimal numbers with a space between.</summary>
    public const string NamePrefix = "mbfp frame ";

    /// <summary>The annotation with the marker's animation time, in nanoseconds: on every slice.</summary>
    public const string AnimationNs = "animationNs";

    /// <summary>The annotation with the marker's preferred frame time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string MarkerPreferredNs = "markerPreferredNs";

    /// <summary>The annotation with the marker's target frame time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string MarkerTargetNs = "markerTargetNs";

    /// <summary>The annotation with the marker's refresh period, in nanoseconds: left out when the marker's is 0.</summary>
    public const string MarkerRefreshPeriodNs = "markerRefreshPeriodNs";

    /// <summary>The annotation with the marker's intended display time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string IntendedDisplayNs = "intendedDisplayNs";

    /// <summary>The clock a slice's times are on: the system's CLOCK_MONOTONIC, by the number Perfetto gives its built-in clocks.</summary>
    public const int ClockId = 3;

    // Part 2 (optional, not checked against the capture): the tracks, under the application's process
    /// <summary>The track of the waits before a frame's work.</summary>
    public const string WaitTrackName = "mbfp waits";

    /// <summary>The track of the present calls.</summary>
    public const string PresentTrackName = "mbfp presents";

    /// <summary>The track of the GPU's work on the frames.</summary>
    public const string GpuTrackName = "mbfp gpu";

    /// <summary>The track of the display times the platform reported.</summary>
    public const string DisplayTrackName = "mbfp display";

    /// <summary>The track of what concerns the run: its environment and its events.</summary>
    public const string RunTrackName = "mbfp run";

    // Part 2: what a slice's or a moment's name begins with, the space included: the frame's run id and frame index follow (FrameDetailName)
    /// <summary>A wait before the frame's CPU work, from its start to its end.</summary>
    public const string WaitNamePrefix = "mbfp wait ";

    /// <summary>The frame's present call, from the call to its return.</summary>
    public const string PresentNamePrefix = "mbfp present ";

    /// <summary>The GPU's work on the frame, from its begin to its end, on the record's clock.</summary>
    public const string GpuNamePrefix = "mbfp gpu ";

    /// <summary>A moment: when the platform says the frame was shown.</summary>
    public const string DisplayNamePrefix = "mbfp display ";

    /// <summary>A moment: a frame that was made and never presented.</summary>
    public const string NotPresentedNamePrefix = "mbfp not presented ";

    // Part 2: the run's moments on RunTrackName, named without ids (the annotation RunId has the run)
    /// <summary>What the run runs on: logged when the run starts and again when any of it changes.</summary>
    public const string EnvironmentName = "mbfp environment";

    /// <summary>The swap chain was made anew; the annotation Reason says why.</summary>
    public const string SwapChainRecreatedName = "mbfp swap chain recreated";

    /// <summary>The swap interval changed: From, To and By.</summary>
    public const string SwapIntervalChangeName = "mbfp swap interval change";

    // Part 2: the annotations
    /// <summary>The run's id, on a moment named without ids.</summary>
    public const string RunId = "runId";

    /// <summary>A wait's kind: one of the WaitKind values.</summary>
    public const string Kind = "kind";

    /// <summary>The time a wait waited for, on the record's clock, where it had one.</summary>
    public const string PlannedNs = "plannedNs";

    /// <summary>The swap interval the present was made with.</summary>
    public const string SwapInterval = "swapInterval";

    /// <summary>What the present returned: one of the PresentResult values.</summary>
    public const string Result = "result";

    /// <summary>The platform's own number for what the present returned, where it has one.</summary>
    public const string ResultCode = "resultCode";

    /// <summary>The time on the present before which the frame is not shown, where it had one.</summary>
    public const string NotBeforeNs = "notBeforeNs";

    /// <summary>The time the frame before stays at least, on the present, where it had one.</summary>
    public const string MinimumDurationNs = "minimumDurationNs";

    /// <summary>How many frames were presented and not yet shown or finished when the present was made.</summary>
    public const string FramesInFlight = "framesInFlight";

    /// <summary>Where a reported display time comes from, in the platform's words.</summary>
    public const string Source = "source";

    /// <summary>Why the swap chain was made anew.</summary>
    public const string Reason = "reason";

    /// <summary>The swap interval before a change.</summary>
    public const string From = "from";

    /// <summary>The swap interval after a change.</summary>
    public const string To = "to";

    /// <summary>Who changed the swap interval: one of the ChangedBy values.</summary>
    public const string By = "by";

    /// <summary>The graphics API: one of the GraphicsApi values.</summary>
    public const string GraphicsApi = "graphicsApi";

    /// <summary>The window system: one of the WindowSystem values.</summary>
    public const string WindowSystem = "windowSystem";

    /// <summary>How the display's side takes presents: one of the PresentMode values.</summary>
    public const string PresentMode = "presentMode";

    /// <summary>How many images the swap chain has.</summary>
    public const string ImageCount = "imageCount";

    /// <summary>The most frames the application lets be in flight.</summary>
    public const string MaxFramesInFlight = "maxFramesInFlight";

    /// <summary>The display's refresh period the application works with.</summary>
    public const string RefreshPeriodNs = "refreshPeriodNs";

    /// <summary>Whether the display's refresh rate is variable: on, off or unknown.</summary>
    public const string VariableRefresh = "variableRefresh";

    /// <summary>What the frame loop is paced by: one of the VsyncSource values.</summary>
    public const string VsyncSource = "vsyncSource";

    /// <summary>The pacer's tier as it is shown, \"3.1\" with its marks, or \"none\".</summary>
    public const string PacerTier = "pacerTier";

    /// <summary>The pacer's aim: smoothness, low latency or none.</summary>
    public const string PacerAim = "pacerAim";

    /// <summary>The pacer's capabilities the platform has, by their names, separated by \", \".</summary>
    public const string Capabilities = "capabilities";

    /// <summary>The pacer's capabilities that are active, the same way.</summary>
    public const string ActiveCapabilities = "activeCapabilities";

    /// <summary>The extensions and protocols found, in the platform's own names, separated by \", \": the only platform words.</summary>
    public const string PlatformFeatures = "platformFeatures";

    // Part 2: the values of WaitKind
    /// <summary>For a frame in flight to finish, so that its slot can be used again.</summary>
    public const string WaitKindFrameSlot = "frame slot";

    /// <summary>For the next image of the swap chain.</summary>
    public const string WaitKindAcquire = "acquire";

    /// <summary>For an earlier present to be shown.</summary>
    public const string WaitKindPresent = "present";

    /// <summary>For the GPU to finish an earlier frame.</summary>
    public const string WaitKindGpu = "gpu";

    /// <summary>Until the time the frame is to start.</summary>
    public const string WaitKindStartTime = "start time";

    /// <summary>For a vertical blank.</summary>
    public const string WaitKindVBlank = "vblank";

    /// <summary>Anything else.</summary>
    public const string WaitKindOther = "other";

    // Part 2: the values of PresentResult
    /// <summary>The present was taken.</summary>
    public const string PresentResultAccepted = "accepted";

    /// <summary>Taken, and the swap chain no longer fits the window as well as it could.</summary>
    public const string PresentResultSuboptimal = "suboptimal";

    /// <summary>Not taken: the swap chain has to be made anew.</summary>
    public const string PresentResultOutOfDate = "out of date";

    /// <summary>Not taken: the surface or the device is lost.</summary>
    public const string PresentResultLost = "lost";

    /// <summary>Not taken, for another reason.</summary>
    public const string PresentResultRefused = "refused";

    /// <summary>Anything else.</summary>
    public const string PresentResultOther = "other";

    // Part 2: the values of PresentMode, in no API's words
    /// <summary>Every present is shown, in order, one a refresh at the most; a full queue makes the present wait (FIFO).</summary>
    public const string PresentModeQueue = "queue";

    /// <summary>The same, but a present that missed its refresh is shown at once and may tear (FIFO relaxed).</summary>
    public const string PresentModeQueueLateAtOnce = "queue, late at once";

    /// <summary>At each refresh the newest finished present is shown and older ones are left out; nothing tears (mailbox).</summary>
    public const string PresentModeNewestAtRefresh = "newest at refresh";

    /// <summary>A present is shown at once, and tears.</summary>
    public const string PresentModeImmediate = "immediate";

    /// <summary>Not known.</summary>
    public const string PresentModeUnknown = "unknown";

    // Part 2: the values of GraphicsApi
    /// <summary>Vulkan.</summary>
    public const string GraphicsApiVulkan = "vulkan";

    /// <summary>OpenGL.</summary>
    public const string GraphicsApiOpenGl = "opengl";

    /// <summary>OpenGL ES.</summary>
    public const string GraphicsApiOpenGlEs = "opengl es";

    /// <summary>Direct3D 11.</summary>
    public const string GraphicsApiDirect3D11 = "direct3d 11";

    /// <summary>Direct3D 12.</summary>
    public const string GraphicsApiDirect3D12 = "direct3d 12";

    /// <summary>Metal.</summary>
    public const string GraphicsApiMetal = "metal";

    /// <summary>Anything else.</summary>
    public const string GraphicsApiOther = "other";

    // Part 2: the values of WindowSystem
    /// <summary>Windows.</summary>
    public const string WindowSystemWin32 = "win32";

    /// <summary>Wayland.</summary>
    public const string WindowSystemWayland = "wayland";

    /// <summary>X11.</summary>
    public const string WindowSystemX11 = "x11";

    /// <summary>Android.</summary>
    public const string WindowSystemAndroid = "android";

    /// <summary>macOS.</summary>
    public const string WindowSystemMacOs = "macos";

    /// <summary>iOS.</summary>
    public const string WindowSystemIos = "ios";

    /// <summary>A display without a window system (KMS).</summary>
    public const string WindowSystemKms = "kms";

    /// <summary>Anything else.</summary>
    public const string WindowSystemOther = "other";

    // Part 2: the values of VsyncSource
    /// <summary>A wait for a present.</summary>
    public const string VsyncSourcePresentWait = "present wait";

    /// <summary>Vertical blank times.</summary>
    public const string VsyncSourceVBlankTimes = "vblank times";

    /// <summary>The display times the platform reports.</summary>
    public const string VsyncSourceDisplayTiming = "display timing";

    /// <summary>A timer and the refresh period.</summary>
    public const string VsyncSourceTimer = "timer";

    /// <summary>Nothing: the loop runs free.</summary>
    public const string VsyncSourceNone = "none";

    // Part 2: the values of VariableRefresh, PacerAim and ChangedBy
    /// <summary>VariableRefresh: the refresh rate is variable.</summary>
    public const string On = "on";

    /// <summary>VariableRefresh: it is not.</summary>
    public const string Off = "off";

    /// <summary>VariableRefresh: not known.</summary>
    public const string Unknown = "unknown";

    /// <summary>PacerAim: the pacer's smoothness aim.</summary>
    public const string AimSmoothness = "smoothness";

    /// <summary>PacerAim: the pacer's low latency aim.</summary>
    public const string AimLowLatency = "low latency";

    /// <summary>PacerTier and PacerAim: no pacer.</summary>
    public const string NoPacer = "none";

    /// <summary>ChangedBy: the pacer's rule.</summary>
    public const string ChangedByPacer = "pacer";

    /// <summary>ChangedBy: the application.</summary>
    public const string ChangedByApplication = "application";
  }
}
