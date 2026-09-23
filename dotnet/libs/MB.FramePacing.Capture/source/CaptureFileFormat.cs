//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The .mbfc capture file: a 64 byte header followed by fixed size records (one per captured frame), so records can be read at random and in
//* parallel. All values are little endian.
//*
//* Header (64 bytes)                              Record (RecordSize bytes, a multiple of 64)
//*   0  "MBFC"                                      0  u64 capture index (the capture card's frame counter, NOT the marker frame index)
//*   4  u16 format version (1)                      8  i64 host ticks   (TimeSpan ticks since capture start, Stopwatch based)
//*   6  u16 header size (64)                       16  i64 device ticks (TimeSpan ticks from the device/driver timestamp, long.MinValue = unknown)
//*   8  u32 width   12 u32 height (stored frame)   24  u32 flags (CaptureRecordFlags)
//*  16  u32 pixel format (1 = Gray8)               28  u32 pixel byte count
//*  20  u32 record header size (32)                32  pixels, width * height bytes, rows packed
//*  24  u32 record size                             .. zero padding up to RecordSize
//*  28  u32 reserved
//*  32  u32 nominal fps numerator  36 u32 denominator (0/0 = unknown)
//*  40  i32 source width   44 i32 source height (the device mode before crop/scale, 0 = unknown)
//*  48  i32 roi x  52 roi y  56 roi width  60 roi height (crop in source pixels applied before scaling, width 0 = none)
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.IO;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public enum CapturePixelFormat : uint
  {
    Gray8 = 1,
  }

  [Flags]
  public enum CaptureRecordFlags : uint
  {
    None = 0,

    /// <summary>The capture source (driver / ffmpeg) reported dropping frames between the previous record and this one.</summary>
    SourceDropBefore = 1,
  }

  /// <summary>A rational frame rate (e.g. 60000/1001).</summary>
  public readonly record struct FrameRate(uint Numerator, uint Denominator)
  {
    public static readonly FrameRate Unknown = new FrameRate(0, 0);

    public bool IsKnown => Numerator > 0 && Denominator > 0;

    public double FramesPerSecond => IsKnown ? (double)Numerator / Denominator : 0;

    /// <summary>Frame interval in TimeSpan ticks, 0 if unknown.</summary>
    public long IntervalTicks => IsKnown ? (long)Math.Round(TimeSpan.TicksPerSecond * (double)Denominator / Numerator) : 0;

    public static FrameRate FromFps(double fps)
    {
      if (fps <= 0)
        return Unknown;
      // Recognize the NTSC style rates exactly, everything else to 1/1000 fps precision
      foreach (uint rate in new uint[] { 24, 30, 48, 60, 120, 240 })
      {
        if (Math.Abs(fps - (rate * 1000.0 / 1001.0)) < 0.0005)
          return new FrameRate(rate * 1000, 1001);
      }
      return Math.Abs(fps - Math.Round(fps)) < 1e-9 ? new FrameRate((uint)Math.Round(fps), 1) : new FrameRate((uint)Math.Round(fps * 1000), 1000);
    }

    public override string ToString() => IsKnown ? (Denominator == 1 ? $"{Numerator}" : $"{FramesPerSecond:0.###}") : "unknown";
  }

  public sealed record CaptureFileHeader(
    int Width,
    int Height,
    FrameRate NominalFrameRate,
    int SourceWidth = 0,
    int SourceHeight = 0,
    PixelRect Roi = default,
    CapturePixelFormat PixelFormat = CapturePixelFormat.Gray8
  )
  {
    public const uint Magic = 0x4346424D; // "MBFC" little endian
    public const ushort FormatVersion = 1;
    public const int HeaderSize = 64;
    public const int RecordHeaderSize = 32;
    public const int RecordAlignment = 64;

    public int PixelByteCount => checked(Width * Height);

    public int RecordSize => ((RecordHeaderSize + PixelByteCount + RecordAlignment - 1) / RecordAlignment) * RecordAlignment;

    public void Write(Span<byte> dst)
    {
      if (dst.Length < HeaderSize)
        throw new ArgumentException("Header buffer too small", nameof(dst));
      dst.Slice(0, HeaderSize).Clear();
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(0), Magic);
      BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(4), FormatVersion);
      BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(6), HeaderSize);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(8), (uint)Width);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(12), (uint)Height);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(16), (uint)PixelFormat);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(20), RecordHeaderSize);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), (uint)RecordSize);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(32), NominalFrameRate.Numerator);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(36), NominalFrameRate.Denominator);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(40), SourceWidth);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(44), SourceHeight);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(48), Roi.X);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(52), Roi.Y);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(56), Roi.Width);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(60), Roi.Height);
    }

    public static CaptureFileHeader Read(ReadOnlySpan<byte> src)
    {
      if (src.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(src) != Magic)
        throw new InvalidDataException("Not an mb-framepacing capture file (.mbfc)");
      ushort version = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4));
      if (version != FormatVersion)
        throw new InvalidDataException($"Unsupported capture file format version {version}");
      if (
        BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(6)) != HeaderSize
        || BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(20)) != RecordHeaderSize
      )
        throw new InvalidDataException("Unexpected capture file header or record header size");

      var pixelFormat = (CapturePixelFormat)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(16));
      if (pixelFormat != CapturePixelFormat.Gray8)
        throw new InvalidDataException($"Unsupported pixel format {pixelFormat}");

      var header = new CaptureFileHeader(
        (int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(8)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(12)),
        new FrameRate(BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(32)), BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(36))),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(40)),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(44)),
        new PixelRect(
          BinaryPrimitives.ReadInt32LittleEndian(src.Slice(48)),
          BinaryPrimitives.ReadInt32LittleEndian(src.Slice(52)),
          BinaryPrimitives.ReadInt32LittleEndian(src.Slice(56)),
          BinaryPrimitives.ReadInt32LittleEndian(src.Slice(60))
        ),
        pixelFormat
      );
      if (header.Width <= 0 || header.Height <= 0)
        throw new InvalidDataException("Invalid frame size in capture file header");
      if (BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)) != header.RecordSize)
        throw new InvalidDataException("Record size in the capture file header does not match the frame size");
      return header;
    }
  }

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
