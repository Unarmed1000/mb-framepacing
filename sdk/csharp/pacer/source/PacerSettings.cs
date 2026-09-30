//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How a FramePacer paces. The display's refresh period is required; every other value has a default, the rule's being Swappy's (Android's
//* frame pacing library, SwappyCommon.cpp, as the mb-framepacing-explained repository describes its rule): they are settings, not properties of
//* frame pacing in general. Always valid: every setter clamps its value into its range (the C++ library asserts, then clamps), and the ranges keep
//* the rule's arithmetic within 64 bits. A pacer copies the settings when it is made.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public sealed class PacerSettings
  {
    public const uint MaxSwapInterval = 100;
    public const long MaxWindowTicks = 60 * TimeSpan.TicksPerSecond;
    public const uint MaxSlowDownLatePercent = 100;
    public const long MaxFrameMarginTicks = TimeSpan.TicksPerSecond;
    public const long MaxSlowestFrameTicks = 10 * TimeSpan.TicksPerSecond;
    public const long MaxPresentLatencyTicks = TimeSpan.TicksPerSecond;
    public const uint MinWindowCapacity = 2;
    public const uint MaxWindowCapacity = 1u << 20;

    private RefreshPeriod m_refresh;
    private uint m_preferredSwapInterval = 1;
    private SlowDownRule m_slowDown = SlowDownRule.LateCount;
    private long m_windowTicks = 2 * TimeSpan.TicksPerSecond;
    private uint m_slowDownLatePercent = 10;
    private long m_frameMarginTicks = TimeSpan.TicksPerMillisecond;
    private long m_slowestFrameTicks = 50 * TimeSpan.TicksPerMillisecond;
    private long m_presentLatencyTicks;
    private uint m_windowCapacity;

    /// <summary>The display's refresh period, from its display mode (a DXGI output mode, Display.getRefreshRate, wl_output's mode).</summary>
    /// <exception cref="ArgumentException">For default(RefreshPeriod).</exception>
    public PacerSettings(RefreshPeriod refresh)
    {
      Refresh = refresh;
    }

    /// <summary>The display's refresh period. Setting default(RefreshPeriod) throws an <see cref="ArgumentException"/>.</summary>
    public RefreshPeriod Refresh
    {
      get => m_refresh;
      set => m_refresh = RequireRefresh(value, nameof(value));
    }

    /// <summary>
    /// The swap interval the application wants, in refreshes: 1 = every refresh (1 to <see cref="MaxSwapInterval"/>). The pacer never goes
    /// faster; its refreshes are the marker's preferred frame time.
    /// </summary>
    public uint PreferredSwapInterval
    {
      get => m_preferredSwapInterval;
      set => m_preferredSwapInterval = Math.Clamp(value, 1u, MaxSwapInterval);
    }

    /// <summary>Adapt the swap interval to the frames (the rule). false: always <see cref="PreferredSwapInterval"/>.</summary>
    public bool AutoSwapInterval { get; set; } = true;

    /// <summary>When the rule slows down (<see cref="SlowDownRule"/>); a value that is none of its members is LateCount.</summary>
    public SlowDownRule SlowDown
    {
      get => m_slowDown;
      set => m_slowDown = value == SlowDownRule.FullWindow ? SlowDownRule.FullWindow : SlowDownRule.LateCount;
    }

    /// <summary>How long a stretch of frames the rule looks at (1 tick to <see cref="MaxWindowTicks"/>).</summary>
    public long WindowTicks
    {
      get => m_windowTicks;
      set => m_windowTicks = Math.Clamp(value, 1, MaxWindowTicks);
    }

    /// <summary>The rule slows down when more than this share of the window's frames was late (percent, 0 to 100).</summary>
    public uint SlowDownLatePercent
    {
      get => m_slowDownLatePercent;
      set => m_slowDownLatePercent = Math.Min(value, MaxSlowDownLatePercent);
    }

    /// <summary>
    /// Added to the frames' average work time before it is compared with swap intervals, and asked for as room to spare to speed up (0 to
    /// <see cref="MaxFrameMarginTicks"/>).
    /// </summary>
    public long FrameMarginTicks
    {
      get => m_frameMarginTicks;
      set => m_frameMarginTicks = Math.Clamp(value, 0, MaxFrameMarginTicks);
    }

    /// <summary>The rule slows down no further once the current swap interval is longer than this plus the margin (0 to <see cref="MaxSlowestFrameTicks"/>).</summary>
    public long SlowestFrameTicks
    {
      get => m_slowestFrameTicks;
      set => m_slowestFrameTicks = Math.Clamp(value, 0, MaxSlowestFrameTicks);
    }

    /// <summary>Without display-time feedback: how long after Present a frame can be shown at the earliest (0 to <see cref="MaxPresentLatencyTicks"/>). 0: at the next refresh.</summary>
    public long PresentLatencyTicks
    {
      get => m_presentLatencyTicks;
      set => m_presentLatencyTicks = Math.Clamp(value, 0, MaxPresentLatencyTicks);
    }

    /// <summary>
    /// Frames the rule's window can hold (<see cref="MinWindowCapacity"/> to <see cref="MaxWindowCapacity"/>); 0: enough for WindowTicks at the
    /// preferred swap interval, twice over (room for a faster display mode). Allocated once, when the pacer is made.
    /// </summary>
    public uint WindowCapacity
    {
      get => m_windowCapacity;
      set => m_windowCapacity = value == 0 ? 0 : Math.Clamp(value, MinWindowCapacity, MaxWindowCapacity);
    }

    /// <summary>A copy, to change without changing these.</summary>
    public PacerSettings Clone() => (PacerSettings)MemberwiseClone();

    internal static RefreshPeriod RequireRefresh(RefreshPeriod period, string parameterName) =>
      period.IsDefault
        ? throw new ArgumentException(
          "default(RefreshPeriod) is not a refresh period: make it with RefreshPeriod.FromRate, FromNanoseconds or FromTicks",
          parameterName
        )
        : period;
  }
}
