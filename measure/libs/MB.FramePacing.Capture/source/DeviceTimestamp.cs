//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame's device timestamp as a source hands it over: the time the capture device, driver or file gave the frame, or the word that there
//* is none (unknown), or that it arrives after the pixels (pending: the recorder waits for it, through IDeviceTimestampSource).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  /// <summary>
  /// A frame's device timestamp: a time on the device's clock, <see cref="Unknown"/> (there is none) or <see cref="Pending"/> (it arrives
  /// after the pixels, through <see cref="IDeviceTimestampSource"/>). Pending only exists between a source and the recorder: files hold a
  /// time or unknown.
  /// </summary>
  public readonly struct DeviceTimestamp : IEquatable<DeviceTimestamp>
  {
    private enum State : byte
    {
      Unknown = 0,
      Pending,
      Known,
    }

    private readonly NanosecondTickCount m_time;
    private readonly State m_state;

    /// <summary>The device gave no timestamp. Also <c>default</c>.</summary>
    public static readonly DeviceTimestamp Unknown;

    /// <summary>The timestamp arrives later, through <see cref="IDeviceTimestampSource"/>.</summary>
    public static readonly DeviceTimestamp Pending = new DeviceTimestamp(State.Pending);

    public DeviceTimestamp(NanosecondTickCount time)
    {
      m_time = time;
      m_state = State.Known;
    }

    private DeviceTimestamp(State state)
    {
      m_time = default;
      m_state = state;
    }

    public bool IsKnown => m_state == State.Known;

    public bool IsPending => m_state == State.Pending;

    /// <summary>The time on the device's clock.</summary>
    /// <exception cref="InvalidOperationException">The timestamp is unknown or pending.</exception>
    public NanosecondTickCount Time => IsKnown ? m_time : throw new InvalidOperationException($"The device timestamp is {this}");

    /// <summary>The time, or null when it is unknown or pending.</summary>
    public NanosecondTickCount? ToNullable() => IsKnown ? m_time : null;

    public bool Equals(DeviceTimestamp other) => m_state == other.m_state && m_time == other.m_time;

    public override bool Equals(object? obj) => obj is DeviceTimestamp other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(m_state, m_time);

    public static bool operator ==(DeviceTimestamp left, DeviceTimestamp right) => left.Equals(right);

    public static bool operator !=(DeviceTimestamp left, DeviceTimestamp right) => !left.Equals(right);

    public override string ToString() =>
      m_state switch
      {
        State.Known => m_time.ToString(),
        State.Pending => "pending",
        _ => "unknown",
      };
  }
}
