#ifndef MB_FRAMEPACING_MARKER_PAYLOAD_PAYLOAD_HPP
#define MB_FRAMEPACING_MARKER_PAYLOAD_PAYLOAD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <cassert>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! The data every marker carries, its fields in the order of the wire format (doc/marker-format.md). The kind, run id, frame index,
  //! flags and animation time are required; the timing fields are optional (0 = unknown). Every time is in nanoseconds, in the type that
  //! says what it is: a span, a point on the pacer's steady clock, or a duration, which is never negative.
  //!
  //! The marker holds its three durations in four bytes each, so a payload holds none longer than a marker can carry: the constructor caps
  //! a longer CPU busy at MaxCpuBusy and a longer frame time at MaxFrameTime (never an error: this runs in a frame loop). So every value
  //! of a payload is valid on the wire, and a payload decodes to exactly what was encoded. The kind must be one of MarkerKind's: the
  //! constructor asserts it, and EncodePayload refuses a payload without one. The flags are kept as given, reserved bits included.
  //! Trivially copyable and standard layout.
  class Payload
  {
  public:
    //! The most bytes a payload encodes to (a start marker's): EncodePayload's buffer size.
    static constexpr std::size_t MaxEncodedByteCount = 81;
    //! The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for.
    //! The largest value the field's four bytes hold (0xFFFFFFFF), so no frame time is that long: see MaxFrameTime.
    static constexpr NanosecondTimeDuration OnDemandFrameTime = NanosecondTimeDuration::FromNanoseconds(4'294'967'295);
    //! The longest target and preferred frame time a marker carries, 4.294967294 s (slower than 0.233 fps): one below OnDemandFrameTime.
    //! A longer one is held as this.
    static constexpr NanosecondTimeDuration MaxFrameTime = NanosecondTimeDuration::FromNanoseconds(4'294'967'294);
    //! The longest CPU busy a marker carries, 4.294967295 s. A longer one is held as this.
    static constexpr NanosecondTimeDuration MaxCpuBusy = NanosecondTimeDuration::FromNanoseconds(4'294'967'295);

    //! An empty payload (a frame marker, everything 0), for decoding into.
    constexpr Payload() noexcept = default;

    constexpr Payload(const MarkerKind kind, const uint32_t runId, const uint64_t frameIndex, const MarkerFlags flags,
                      const NanosecondTimeSpan animationTime, const NanosecondTimeDuration preferredFrameTime = {},
                      const NanosecondTimeDuration targetFrameTime = {}, const NanosecondTickCount intendedDisplayTime = {},
                      const NanosecondTickCount cpuStartTime = {}, const NanosecondTimeDuration cpuBusy = {}) noexcept
      : m_kind(kind)
      , m_runId(runId)
      , m_frameIndex(frameIndex)
      , m_flags(flags)
      , m_animationTime(animationTime)
      , m_preferredFrameTime(CappedFrameTime(preferredFrameTime))
      , m_targetFrameTime(CappedFrameTime(targetFrameTime))
      , m_intendedDisplayTime(intendedDisplayTime)
      , m_cpuStartTime(cpuStartTime)
      , m_cpuBusy(NanosecondTimeDuration::Min(cpuBusy, MaxCpuBusy))
    {
      assert(kind <= MarkerKind::Sync);
    }

    //! The same payload with another kind: a start or end marker carries the values of the frame that shows it, a sync marker its run id
    //! and frame index.
    [[nodiscard]] constexpr Payload WithKind(const MarkerKind kind) const noexcept
    {
      return {kind,           m_runId,  m_frameIndex, m_flags, m_animationTime, m_preferredFrameTime, m_targetFrameTime, m_intendedDisplayTime,
              m_cpuStartTime, m_cpuBusy};
    }

    [[nodiscard]] constexpr MarkerKind Kind() const noexcept
    {
      return m_kind;
    }

    //! Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.
    [[nodiscard]] constexpr uint32_t RunId() const noexcept
    {
      return m_runId;
    }

    //! The application's own rendered-frame counter. Unrelated to the capture card's frame counter.
    [[nodiscard]] constexpr uint64_t FrameIndex() const noexcept
    {
      return m_frameIndex;
    }

    //! MarkerFlags::StaticAfter when nothing animates while this frame is on screen, MarkerFlags::StaticBefore when nothing animated while
    //! the frame before it was; the other bits are reserved (write 0, a decoded payload keeps them).
    [[nodiscard]] constexpr MarkerFlags Flags() const noexcept
    {
      return m_flags;
    }

    //! The animation time: the time on the application's animation clock the frame's animation was evaluated for.
    [[nodiscard]] constexpr NanosecondTimeSpan AnimationTime() const noexcept
    {
      return m_animationTime;
    }

    //! The interval the application wants to run at: what it would aim for if nothing held it back. It differs from TargetFrameTime only
    //! while the pacer runs slower than it wants (a pacer lowered to 30 fps: preferred 16'666'667 ns, target 33'333'333). A 30 fps lock or
    //! a device idle at 1 fps prefers what it runs at. 0 = unknown, OnDemandFrameTime = frames only when something changes; at most
    //! MaxFrameTime otherwise.
    [[nodiscard]] constexpr NanosecondTimeDuration PreferredFrameTime() const noexcept
    {
      return m_preferredFrameTime;
    }

    //! The interval the frame pacer aims for between the previous frame and this one: 16'666'667 ns for 60 fps. 0 = unknown,
    //! OnDemandFrameTime = frames only when something changes; at most MaxFrameTime otherwise.
    [[nodiscard]] constexpr NanosecondTimeDuration TargetFrameTime() const noexcept
    {
      return m_targetFrameTime;
    }

    //! When the frame pacer intends this frame to become visible, on its steady clock (any epoch, the same clock for the whole run).
    //! 0 = unknown.
    [[nodiscard]] constexpr NanosecondTickCount IntendedDisplayTime() const noexcept
    {
      return m_intendedDisplayTime;
    }

    //! CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), on the same steady clock as
    //! IntendedDisplayTime. Anywhere inside a refresh; frames can overlap. 0 = unknown.
    [[nodiscard]] constexpr NanosecondTickCount CpuStartTime() const noexcept
    {
      return m_cpuStartTime;
    }

    //! CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from CpuStartTime until Present is
    //! called. The marker is drawn last, so the application measures it as it draws the marker. It does not include the GPU's work. May
    //! span several refreshes. 0 = unknown; at most MaxCpuBusy.
    [[nodiscard]] constexpr NanosecondTimeDuration CpuBusy() const noexcept
    {
      return m_cpuBusy;
    }

    constexpr bool operator==(const Payload&) const noexcept = default;

  private:
    //! A frame time as a marker carries it: on demand as it is, any other one at most MaxFrameTime, so that only on demand reads as on
    //! demand.
    static constexpr NanosecondTimeDuration CappedFrameTime(const NanosecondTimeDuration frameTime) noexcept
    {
      return frameTime == OnDemandFrameTime ? frameTime : NanosecondTimeDuration::Min(frameTime, MaxFrameTime);
    }

    MarkerKind m_kind{MarkerKind::Frame};
    uint32_t m_runId{0};
    uint64_t m_frameIndex{0};
    MarkerFlags m_flags{MarkerFlags::NoFlags};
    NanosecondTimeSpan m_animationTime;
    NanosecondTimeDuration m_preferredFrameTime;
    NanosecondTimeDuration m_targetFrameTime;
    NanosecondTickCount m_intendedDisplayTime;
    NanosecondTickCount m_cpuStartTime;
    NanosecondTimeDuration m_cpuBusy;
  };
}

#endif
