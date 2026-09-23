//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The per-frame record header of a .mbfc capture file: capture index, host and device timestamps and flags.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;

namespace MB.FramePacing.Capture
{
  /// <summary>The per-frame record header.</summary>
  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to the frame index encoded in the marker.</param>
  /// <param name="HostTicks">TimeSpan ticks since capture start when the frame arrived in this process.</param>
  /// <param name="DeviceTicks">TimeSpan ticks from the device/driver timestamp, <see cref="UnknownTicks"/> if not available.</param>
  public readonly record struct CaptureRecordHeader(long CaptureIndex, long HostTicks, long DeviceTicks, CaptureRecordFlags Flags, int PixelByteCount)
  {
    public const long UnknownTicks = long.MinValue;

    public bool HasDeviceTicks => DeviceTicks != UnknownTicks;

    public void Write(Span<byte> dst)
    {
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(0), CaptureIndex);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(8), HostTicks);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(16), DeviceTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), (uint)Flags);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(28), (uint)PixelByteCount);
    }

    public static CaptureRecordHeader Read(ReadOnlySpan<byte> src) =>
      new CaptureRecordHeader(
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(0)),
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(8)),
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(16)),
        (CaptureRecordFlags)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(28))
      );
  }
}
