//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The per-frame record header of a .mbfc capture file: capture index, host and device timestamps and flags.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using MB.FramePacing.Data;

namespace MB.FramePacing.Capture
{
  /// <summary>The per-frame record header.</summary>
  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to the frame index encoded in the marker.</param>
  /// <param name="HostTime">When the frame arrived in this process, on the capture clock (the time since the capture started).</param>
  /// <param name="DeviceTime">The device or driver's timestamp. A file holds a time or unknown; pending only exists in the recorder's ring.</param>
  /// <param name="SourceDrops">How many frames the source reported dropping since the previous record.</param>
  public readonly record struct CaptureRecordHeader(
    long CaptureIndex,
    TickCount64 HostTime,
    DeviceTimestamp DeviceTime,
    uint SourceDrops,
    int PixelByteCount
  )
  {
    /// <summary>The device time's bytes for unknown, as the capture data format has it.</summary>
    private const long UnknownTicks = long.MinValue;

    /// <summary>The device time's bytes for pending: never in a file.</summary>
    private const long PendingTicks = long.MinValue + 1;

    public void Write(Span<byte> dst)
    {
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(0), CaptureIndex);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(8), HostTime.Ticks);
      BinaryPrimitives.WriteInt64LittleEndian(
        dst.Slice(16),
        DeviceTime.IsKnown ? DeviceTime.Time.Ticks
          : DeviceTime.IsPending ? PendingTicks
          : UnknownTicks
      );
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), SourceDrops);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(28), (uint)PixelByteCount);
    }

    public static CaptureRecordHeader Read(ReadOnlySpan<byte> src) =>
      new CaptureRecordHeader(
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(0)),
        new TickCount64(BinaryPrimitives.ReadInt64LittleEndian(src.Slice(8))),
        ReadDeviceTime(BinaryPrimitives.ReadInt64LittleEndian(src.Slice(16))),
        BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(28))
      );

    private static DeviceTimestamp ReadDeviceTime(long ticks) =>
      ticks switch
      {
        UnknownTicks => DeviceTimestamp.Unknown,
        PendingTicks => DeviceTimestamp.Pending,
        _ => new DeviceTimestamp(new TickCount64(ticks)),
      };
  }
}
