//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The .mbfc capture file: a header (128 bytes as written) followed by fixed size records (one per captured frame), so records can be read
//* at random and in parallel. All values are little endian.
//*
//* The header says how long it is, and the records start right after it. Its first 64 bytes are what every file has; a field after them
//* is there when the header is long enough to hold it, and is "none" otherwise: a file with a 64 byte header has no second region.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  // The header's layout (the regions and sizes are what captures.mbcd's header has, sdk/doc/capture-data-format.md):
  //    0 magic "MBFC" (u32)    4 format version (u16)    6 header size (u16)
  //    8 stored width, height (u32 each)                16 pixel format (u32)
  //   20 record header size (u32)                       24 record size (u32)                28 reserved (0)
  //   32 nominal frame rate: numerator, denominator (u32 each)
  //   40 source width, height (i32 each, 0 = unknown)
  //   48 region of the source that was stored: x, y, width, height (i32 each, all 0 = the whole frame)
  //   64 second region of the source, stored below the first: x, y, width, height (i32 each, all 0 = none)
  //   80 reserved (0), to the header's end
  public sealed record CaptureFileHeader(
    int Width,
    int Height,
    FrameRate NominalFrameRate,
    int SourceWidth = 0,
    int SourceHeight = 0,
    PixelRect Roi = default,
    PixelRect SyncRoi = default,
    CapturePixelFormat PixelFormat = CapturePixelFormat.Gray8
  )
  {
    public const uint Magic = 0x4346424D; // "MBFC" little endian
    public const ushort FormatVersion = 1;

    /// <summary>The header as it is written.</summary>
    public const int HeaderSize = 128;

    /// <summary>The fields every file has: the shortest header a file can have.</summary>
    public const int MinHeaderSize = 64;

    /// <summary>Where the header's fields after <see cref="MinHeaderSize"/> end: the second region.</summary>
    private const int SyncRoiEnd = 80;
    public const int RecordHeaderSize = 32;
    public const int RecordAlignment = 64;

    public int PixelByteCount => checked(Width * Height);

    public int RecordSize => ((RecordHeaderSize + PixelByteCount + RecordAlignment - 1) / RecordAlignment) * RecordAlignment;

    /// <summary>
    /// The frames' size, for people: the source's, and what was stored of it when that is another size (only the markers' regions, a
    /// region or a downscale): "1920x1080 source, 165x282 stored". A capture that does not know its source's size has the stored one.
    /// </summary>
    public string SizeText =>
      SourceWidth > 0 && SourceHeight > 0 && (SourceWidth != Width || SourceHeight != Height)
        ? string.Create(CultureInfo.InvariantCulture, $"{SourceWidth}x{SourceHeight} source, {Width}x{Height} stored")
        : string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");

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
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(64), SyncRoi.X);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(68), SyncRoi.Y);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(72), SyncRoi.Width);
      BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(76), SyncRoi.Height);
    }

    public static CaptureFileHeader Read(ReadOnlySpan<byte> src) => Read(src, out _);

    /// <summary>
    /// The header at the start of <paramref name="src"/> (at least <see cref="MinHeaderSize"/> bytes, and the whole header up to
    /// <see cref="HeaderSize"/>); <paramref name="headerSize"/> is how long the file's header is, which is where its records start.
    /// </summary>
    public static CaptureFileHeader Read(ReadOnlySpan<byte> src, out int headerSize)
    {
      if (src.Length < MinHeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(src) != Magic)
        throw new InvalidDataException("Not an mb-framepacing capture file (.mbfc)");
      ushort version = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4));
      if (version != FormatVersion)
        throw new InvalidDataException($"Unsupported capture file format version {version}");
      headerSize = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(6));
      if (headerSize < MinHeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(20)) != RecordHeaderSize)
        throw new InvalidDataException("Unexpected capture file header or record header size");
      if (src.Length < Math.Min(headerSize, HeaderSize))
        throw new InvalidDataException("The capture file ends inside its header");
      // The second region is there when the header is long enough to hold it
      var syncRoi =
        headerSize >= SyncRoiEnd
          ? new PixelRect(
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(64)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(68)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(72)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(76))
          )
          : default;

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
        syncRoi,
        pixelFormat
      );
      if (header.Width <= 0 || header.Height <= 0)
        throw new InvalidDataException("Invalid frame size in capture file header");
      if (BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)) != header.RecordSize)
        throw new InvalidDataException("Record size in the capture file header does not match the frame size");
      return header;
    }
  }
}
