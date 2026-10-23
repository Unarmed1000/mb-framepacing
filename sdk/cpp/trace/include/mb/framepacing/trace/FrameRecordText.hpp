#ifndef MB_FRAMEPACING_TRACE_FRAMERECORDTEXT_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORDTEXT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string_view>

//! The strings of the frame record, the slice an application logs
//! to its trace for every frame: the category, the name of the track, the start of a slice's name and the names of the
//! annotations. The tools find the record by these strings, so an application logs them as they are. Every text is a string
//! literal: it is there for the life of the program, nothing is allocated, and its data() ends with a zero, so it can be given to
//! a function that takes a C string.
namespace MB::FramePacing::Trace::FrameRecordText
{
  //! The category of every slice.
  inline constexpr std::string_view Category = "mbfp";
  //! The name of the track the slices are on, under the application's process.
  inline constexpr std::string_view TrackName = "mbfp frames";
  //! What a slice's name begins with, the space included: the frame's run id and frame index follow (FrameRecordName).
  inline constexpr std::string_view NamePrefix = "mbfp frame ";

  //! The annotation with the marker's animation time.
  inline constexpr std::string_view AnimationNs = "animationNs";
  //! The annotation with the marker's preferred frame time.
  inline constexpr std::string_view MarkerPreferredNs = "markerPreferredNs";
  //! The annotation with the marker's target frame time.
  inline constexpr std::string_view MarkerTargetNs = "markerTargetNs";
  //! The annotation with the marker's refresh period.
  inline constexpr std::string_view MarkerRefreshPeriodNs = "markerRefreshPeriodNs";
  //! The annotation with the marker's intended display time.
  inline constexpr std::string_view IntendedDisplayNs = "intendedDisplayNs";

  //! The clock a slice's times are on: Perfetto's built-in clock for the system's CLOCK_MONOTONIC, which the times in the marker
  //! are read from.
  inline constexpr uint32_t ClockId = 3;

  // Part 2 (optional, not checked against the capture): the tracks, under the application's process
  //! The track of the waits before a frame's work.
  inline constexpr std::string_view WaitTrackName = "mbfp waits";
  //! The track of the present calls.
  inline constexpr std::string_view PresentTrackName = "mbfp presents";
  //! The track of the GPU's work on the frames.
  inline constexpr std::string_view GpuTrackName = "mbfp gpu";
  //! The track of the display times the platform reported.
  inline constexpr std::string_view DisplayTrackName = "mbfp display";
  //! The track of what concerns the run: its environment and its events.
  inline constexpr std::string_view RunTrackName = "mbfp run";

  // Part 2: what a slice's or a moment's name begins with, the space included: the frame's run id and frame index follow (FrameDetailName)
  //! A wait before the frame's CPU work, from its start to its end.
  inline constexpr std::string_view WaitNamePrefix = "mbfp wait ";
  //! The frame's present call, from the call to its return.
  inline constexpr std::string_view PresentNamePrefix = "mbfp present ";
  //! The GPU's work on the frame, from its begin to its end, on the record's clock.
  inline constexpr std::string_view GpuNamePrefix = "mbfp gpu ";
  //! A moment: when the platform says the frame was shown.
  inline constexpr std::string_view DisplayNamePrefix = "mbfp display ";
  //! A moment: a frame that was made and never presented.
  inline constexpr std::string_view NotPresentedNamePrefix = "mbfp not presented ";

  // Part 2: the run's moments on RunTrackName, named without ids (the annotation RunId has the run)
  //! What the run runs on: logged when the run starts and again when any of it changes.
  inline constexpr std::string_view EnvironmentName = "mbfp environment";
  //! The swap chain was made anew; the annotation Reason says why.
  inline constexpr std::string_view SwapChainRecreatedName = "mbfp swap chain recreated";
  //! The swap interval changed: From, To and By.
  inline constexpr std::string_view SwapIntervalChangeName = "mbfp swap interval change";

  // Part 2: the annotations
  //! The run's id, on a moment named without ids.
  inline constexpr std::string_view RunId = "runId";
  //! A wait's kind: one of the WaitKind values.
  inline constexpr std::string_view Kind = "kind";
  //! The time a wait waited for, on the record's clock, where it had one.
  inline constexpr std::string_view PlannedNs = "plannedNs";
  //! The swap interval the present was made with.
  inline constexpr std::string_view SwapInterval = "swapInterval";
  //! What the present returned: one of the PresentResult values.
  inline constexpr std::string_view Result = "result";
  //! The platform's own number for what the present returned, where it has one.
  inline constexpr std::string_view ResultCode = "resultCode";
  //! The time on the present before which the frame is not shown, where it had one.
  inline constexpr std::string_view NotBeforeNs = "notBeforeNs";
  //! The time the frame before stays at least, on the present, where it had one.
  inline constexpr std::string_view MinimumDurationNs = "minimumDurationNs";
  //! How many frames were presented and not yet shown or finished when the present was made.
  inline constexpr std::string_view FramesInFlight = "framesInFlight";
  //! Where a reported display time comes from, in the platform's words.
  inline constexpr std::string_view Source = "source";
  //! Why the swap chain was made anew.
  inline constexpr std::string_view Reason = "reason";
  //! The swap interval before a change.
  inline constexpr std::string_view From = "from";
  //! The swap interval after a change.
  inline constexpr std::string_view To = "to";
  //! Who changed the swap interval: one of the ChangedBy values.
  inline constexpr std::string_view By = "by";
  //! The graphics API: one of the GraphicsApi values.
  inline constexpr std::string_view GraphicsApi = "graphicsApi";
  //! The window system: one of the WindowSystem values.
  inline constexpr std::string_view WindowSystem = "windowSystem";
  //! How the display's side takes presents: one of the PresentMode values.
  inline constexpr std::string_view PresentMode = "presentMode";
  //! How many images the swap chain has.
  inline constexpr std::string_view ImageCount = "imageCount";
  //! The most frames the application lets be in flight.
  inline constexpr std::string_view MaxFramesInFlight = "maxFramesInFlight";
  //! The display's refresh period the application works with.
  inline constexpr std::string_view RefreshPeriodNs = "refreshPeriodNs";
  //! Whether the display's refresh rate is variable: on, off or unknown.
  inline constexpr std::string_view VariableRefresh = "variableRefresh";
  //! What the frame loop is paced by: one of the VsyncSource values.
  inline constexpr std::string_view VsyncSource = "vsyncSource";
  //! The pacer's tier as it is shown, \"3.1\" with its marks, or \"none\".
  inline constexpr std::string_view PacerTier = "pacerTier";
  //! The pacer's aim: smoothness, low latency or none.
  inline constexpr std::string_view PacerAim = "pacerAim";
  //! The pacer's capabilities the platform has, by their names, separated by \", \".
  inline constexpr std::string_view Capabilities = "capabilities";
  //! The pacer's capabilities that are active, the same way.
  inline constexpr std::string_view ActiveCapabilities = "activeCapabilities";
  //! The extensions and protocols found, in the platform's own names, separated by \", \": the only platform words.
  inline constexpr std::string_view PlatformFeatures = "platformFeatures";

  // Part 2: the values of WaitKind
  //! For a frame in flight to finish, so that its slot can be used again.
  inline constexpr std::string_view WaitKindFrameSlot = "frame slot";
  //! For the next image of the swap chain.
  inline constexpr std::string_view WaitKindAcquire = "acquire";
  //! For an earlier present to be shown.
  inline constexpr std::string_view WaitKindPresent = "present";
  //! For the GPU to finish an earlier frame.
  inline constexpr std::string_view WaitKindGpu = "gpu";
  //! Until the time the frame is to start.
  inline constexpr std::string_view WaitKindStartTime = "start time";
  //! For a vertical blank.
  inline constexpr std::string_view WaitKindVBlank = "vblank";
  //! Anything else.
  inline constexpr std::string_view WaitKindOther = "other";

  // Part 2: the values of PresentResult
  //! The present was taken.
  inline constexpr std::string_view PresentResultAccepted = "accepted";
  //! Taken, and the swap chain no longer fits the window as well as it could.
  inline constexpr std::string_view PresentResultSuboptimal = "suboptimal";
  //! Not taken: the swap chain has to be made anew.
  inline constexpr std::string_view PresentResultOutOfDate = "out of date";
  //! Not taken: the surface or the device is lost.
  inline constexpr std::string_view PresentResultLost = "lost";
  //! Not taken, for another reason.
  inline constexpr std::string_view PresentResultRefused = "refused";
  //! Anything else.
  inline constexpr std::string_view PresentResultOther = "other";

  // Part 2: the values of PresentMode, in no API's words
  //! Every present is shown, in order, one a refresh at the most; a full queue makes the present wait (FIFO).
  inline constexpr std::string_view PresentModeQueue = "queue";
  //! The same, but a present that missed its refresh is shown at once and may tear (FIFO relaxed).
  inline constexpr std::string_view PresentModeQueueLateAtOnce = "queue, late at once";
  //! At each refresh the newest finished present is shown and older ones are left out; nothing tears (mailbox).
  inline constexpr std::string_view PresentModeNewestAtRefresh = "newest at refresh";
  //! A present is shown at once, and tears.
  inline constexpr std::string_view PresentModeImmediate = "immediate";
  //! Not known.
  inline constexpr std::string_view PresentModeUnknown = "unknown";

  // Part 2: the values of GraphicsApi
  //! Vulkan.
  inline constexpr std::string_view GraphicsApiVulkan = "vulkan";
  //! OpenGL.
  inline constexpr std::string_view GraphicsApiOpenGl = "opengl";
  //! OpenGL ES.
  inline constexpr std::string_view GraphicsApiOpenGlEs = "opengl es";
  //! Direct3D 11.
  inline constexpr std::string_view GraphicsApiDirect3D11 = "direct3d 11";
  //! Direct3D 12.
  inline constexpr std::string_view GraphicsApiDirect3D12 = "direct3d 12";
  //! Metal.
  inline constexpr std::string_view GraphicsApiMetal = "metal";
  //! Anything else.
  inline constexpr std::string_view GraphicsApiOther = "other";

  // Part 2: the values of WindowSystem
  //! Windows.
  inline constexpr std::string_view WindowSystemWin32 = "win32";
  //! Wayland.
  inline constexpr std::string_view WindowSystemWayland = "wayland";
  //! X11.
  inline constexpr std::string_view WindowSystemX11 = "x11";
  //! Android.
  inline constexpr std::string_view WindowSystemAndroid = "android";
  //! macOS.
  inline constexpr std::string_view WindowSystemMacOs = "macos";
  //! iOS.
  inline constexpr std::string_view WindowSystemIos = "ios";
  //! A display without a window system (KMS).
  inline constexpr std::string_view WindowSystemKms = "kms";
  //! Anything else.
  inline constexpr std::string_view WindowSystemOther = "other";

  // Part 2: the values of VsyncSource
  //! A wait for a present.
  inline constexpr std::string_view VsyncSourcePresentWait = "present wait";
  //! Vertical blank times.
  inline constexpr std::string_view VsyncSourceVBlankTimes = "vblank times";
  //! The display times the platform reports.
  inline constexpr std::string_view VsyncSourceDisplayTiming = "display timing";
  //! A timer and the refresh period.
  inline constexpr std::string_view VsyncSourceTimer = "timer";
  //! Nothing: the loop runs free.
  inline constexpr std::string_view VsyncSourceNone = "none";

  // Part 2: the values of VariableRefresh, PacerAim and ChangedBy
  //! VariableRefresh: the refresh rate is variable.
  inline constexpr std::string_view On = "on";
  //! VariableRefresh: it is not.
  inline constexpr std::string_view Off = "off";
  //! VariableRefresh: not known.
  inline constexpr std::string_view Unknown = "unknown";
  //! PacerAim: the pacer's smoothness aim.
  inline constexpr std::string_view AimSmoothness = "smoothness";
  //! PacerAim: the pacer's low latency aim.
  inline constexpr std::string_view AimLowLatency = "low latency";
  //! PacerTier and PacerAim: no pacer.
  inline constexpr std::string_view NoPacer = "none";
  //! ChangedBy: the pacer's rule.
  inline constexpr std::string_view ChangedByPacer = "pacer";
  //! ChangedBy: the application.
  inline constexpr std::string_view ChangedByApplication = "application";
}

#endif
