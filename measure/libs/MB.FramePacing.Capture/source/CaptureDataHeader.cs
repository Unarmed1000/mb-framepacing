//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The header of captures.mbcd: the frames the data was read from (size, source size, region, nominal rate), where the markers were
//* (the layout the decoder locked onto) and whether the frames themselves were stored in frames.mbfc. 256 bytes, little endian, see
//* doc/capture-data-format.md.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  /// <param name="Frames">The captured frames the markers were read from (the header frames.mbfc has, or would have).</param>
  /// <param name="Locks">Where the markers were (the main marker first); empty when none was found.</param>
  /// <param name="FramesStored">The frames were also stored, in frames.mbfc.</param>
  /// <param name="Camera">EXPERIMENTAL: a camera capture (the frames are the rig's rectified zones).</param>
  public sealed record CaptureDataHeader(CaptureFileHeader Frames, IReadOnlyList<MarkerLock> Locks, bool FramesStored, bool Camera)
  {
    public const uint Magic = 0x4443424D; // "MBCD" little endian
    public const ushort FormatVersion = 1;
    public const int HeaderSize = 256;
    public const int MaxLocks = 4;

    private const int LocksOffset = 72;
    private const int LockSize = 24;
    private const uint FramesStoredFlag = 1;
    private const uint CameraFlag = 2;

    public void Write(Span<byte> dst)
    {
      if (dst.Length < HeaderSize)
        throw new ArgumentException("Header buffer too small", nameof(dst));
      if (Locks.Count > MaxLocks)
        throw new ArgumentException($"At most {MaxLocks} marker locks fit the capture data header");
      dst.Slice(0, HeaderSize).Clear();
      BinaryPrimitives.WriteUInt32LittleEndian(dst, Magic);
      BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(4), FormatVersion);
      BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(6), HeaderSize);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(8), CaptureDataRecord.Size);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(12), (FramesStored ? FramesStoredFlag : 0) | (Camera ? CameraFlag : 0));
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(16), Frames.Width);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(20), Frames.Height);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), Frames.NominalFrameRate.Numerator);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(28), Frames.NominalFrameRate.Denominator);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(32), Frames.SourceWidth);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(36), Frames.SourceHeight);
      WriteRect(dst.Slice(40), Frames.Roi);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(64), (uint)Locks.Count);
      for (int i = 0; i < Locks.Count; ++i)
      {
        var lockSlot = dst.Slice(LocksOffset + (i * LockSize));
        WriteRect(lockSlot, Locks[i].Bounds);
        BinaryPrimitives.WriteDoubleLittleEndian(lockSlot.Slice(16), Locks[i].ModuleSizePx);
      }
    }

    public static CaptureDataHeader Read(ReadOnlySpan<byte> src)
    {
      if (src.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(src) != Magic)
        throw new InvalidDataException("Not an mb-framepacing capture data file (.mbcd)");
      ushort version = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4));
      if (version > FormatVersion)
        throw new InvalidDataException(
          $"The capture data file has format version {version}, newer than this tool reads ({FormatVersion}): update the tools"
        );
      if (version != FormatVersion)
        throw new InvalidDataException($"Unsupported capture data file format version {version}");
      if (
        BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(6)) != HeaderSize
        || BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(8)) != CaptureDataRecord.Size
      )
        throw new InvalidDataException("Unexpected capture data header or record size");

      uint flags = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(12));
      var frames = new CaptureFileHeader(
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(16)),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(20)),
        new FrameRate(BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)), BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(28))),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(32)),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(36)),
        ReadRect(src.Slice(40))
      );
      uint lockCount = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(64));
      if (lockCount > MaxLocks)
        throw new InvalidDataException("Invalid marker lock count in the capture data header");
      var locks = new List<MarkerLock>();
      for (int i = 0; i < lockCount; ++i)
      {
        var lockSlot = src.Slice(LocksOffset + (i * LockSize));
        locks.Add(new MarkerLock(ReadRect(lockSlot), BinaryPrimitives.ReadDoubleLittleEndian(lockSlot.Slice(16))));
      }
      return new CaptureDataHeader(frames, locks, (flags & FramesStoredFlag) != 0, (flags & CameraFlag) != 0);
    }

    private static void WriteRect(Span<byte> dst, PixelRect rect)
    {
      BinaryPrimitives.WriteInt32LittleEndian(dst, rect.X);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(4), rect.Y);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(8), rect.Width);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(12), rect.Height);
    }

    private static PixelRect ReadRect(ReadOnlySpan<byte> src) =>
      new PixelRect(
        BinaryPrimitives.ReadInt32LittleEndian(src),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(4)),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(8)),
        BinaryPrimitives.ReadInt32LittleEndian(src.Slice(12))
      );
  }
}
