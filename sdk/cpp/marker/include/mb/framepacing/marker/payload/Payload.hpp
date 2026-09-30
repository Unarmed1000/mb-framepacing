#ifndef MB_FRAMEPACING_MARKER_PAYLOAD_PAYLOAD_HPP
#define MB_FRAMEPACING_MARKER_PAYLOAD_PAYLOAD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! The data every marker carries, its fields in the order of the wire format (doc/marker-format.md). The kind, run id, frame index,
  //! flags and animation time are required; the timing fields are optional (0 = unknown). Trivially copyable and standard layout.
  struct Payload
  {
    //! The most bytes a payload encodes to (a start marker's): EncodePayload's buffer size.
    static constexpr std::size_t MaxEncodedByteCount = 77;
    //! The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for.
    static constexpr uint32_t OnDemandFrameTicks = 0xFFFF'FFFFu;

    //! An empty payload (a frame marker, everything 0), for decoding into.
    constexpr Payload() noexcept = default;

    constexpr Payload(const MarkerKind kind, const uint32_t runId, const uint64_t frameIndex, const MarkerFlags flags, const int64_t animationTicks,
                      const uint32_t preferredFrameTicks = 0, const uint32_t targetFrameTicks = 0, const int64_t intendedDisplayTicks = 0,
                      const int64_t cpuStartTicks = 0, const uint32_t cpuBusyTicks = 0) noexcept
      : Kind(kind)
      , RunId(runId)
      , FrameIndex(frameIndex)
      , Flags(flags)
      , AnimationTicks(animationTicks)
      , PreferredFrameTicks(preferredFrameTicks)
      , TargetFrameTicks(targetFrameTicks)
      , IntendedDisplayTicks(intendedDisplayTicks)
      , CpuStartTicks(cpuStartTicks)
      , CpuBusyTicks(cpuBusyTicks)
    {
    }

    MarkerKind Kind{MarkerKind::Frame};
    //! Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.
    uint32_t RunId{0};
    //! The application's own rendered-frame counter. Unrelated to the capture card's frame counter.
    uint64_t FrameIndex{0};
    //! MarkerFlags::StaticAfter when nothing animates while this frame is on screen, MarkerFlags::StaticBefore when nothing animated while
    //! the frame before it was; the other bits are reserved (0).
    MarkerFlags Flags{MarkerFlags::None};
    //! Animation time in C# TimeSpan ticks (100ns).
    int64_t AnimationTicks{0};
    //! The interval the application wants to run at, in ticks (100ns): what it would aim for if nothing held it back. It differs from
    //! TargetFrameTicks only while the pacer runs slower than it wants (Swappy lowered to 30 fps: preferred 166'667, target 333'333). A 30
    //! fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, OnDemandFrameTicks = frames only when something changes.
    uint32_t PreferredFrameTicks{0};
    //! The interval the frame pacer aims for between the previous frame and this one, in ticks (100ns): 166'667 for 60 fps. 0 = unknown,
    //! OnDemandFrameTicks = frames only when something changes.
    uint32_t TargetFrameTicks{0};
    //! When the frame pacer intends this frame to become visible, in ticks (100ns) on its steady clock (any epoch, the same clock for
    //! the whole run). 0 = unknown.
    int64_t IntendedDisplayTicks{0};
    //! CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), in ticks (100ns) on the same steady
    //! clock as IntendedDisplayTicks. Anywhere inside a refresh; frames can overlap. 0 = unknown.
    int64_t CpuStartTicks{0};
    //! CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from CpuStartTicks until Present
    //! is called, in ticks (100ns). The marker is drawn last, so the application measures it as it draws the marker. It does not
    //! include the GPU's work. May span several refreshes. 0 = unknown.
    uint32_t CpuBusyTicks{0};

    constexpr bool operator==(const Payload&) const noexcept = default;
  };
}

#endif
