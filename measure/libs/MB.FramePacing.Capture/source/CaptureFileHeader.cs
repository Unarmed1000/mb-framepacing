//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The .mbfc capture file: a 64 byte header followed by fixed size records (one per captured frame), so records can be read at random and in
//* parallel. All values are little endian.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.IO;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
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
}
