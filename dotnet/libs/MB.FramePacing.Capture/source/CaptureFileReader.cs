//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Random access reader for .mbfc files. All reads are positional (RandomAccess), so one reader can be shared by parallel decoder threads.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using MB.FramePacing.Marker;
using Microsoft.Win32.SafeHandles;

namespace MB.FramePacing.Capture
{
  public sealed class CaptureFileReader : IDisposable
  {
    private readonly SafeFileHandle m_handle;

    public CaptureFileReader(string path)
    {
      m_handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
      try
      {
        Span<byte> headerBytes = stackalloc byte[CaptureFileHeader.HeaderSize];
        if (RandomAccess.Read(m_handle, headerBytes, 0) != CaptureFileHeader.HeaderSize)
          throw new InvalidDataException($"'{path}' is too short to be a capture file");
        Header = CaptureFileHeader.Read(headerBytes);
        long payloadLength = RandomAccess.GetLength(m_handle) - CaptureFileHeader.HeaderSize;
        // A capture that was killed mid-write may end with a partial record; it is ignored
        RecordCount = payloadLength / Header.RecordSize;
      }
      catch
      {
        m_handle.Dispose();
        throw;
      }
    }

    public CaptureFileHeader Header { get; }

    public long RecordCount { get; }

    public CaptureRecordHeader ReadRecordHeader(long recordIndex)
    {
      Span<byte> buffer = stackalloc byte[CaptureFileHeader.RecordHeaderSize];
      ReadExactly(buffer, RecordOffset(recordIndex));
      return CaptureRecordHeader.Read(buffer);
    }

    /// <summary>Read a record's header and pixels. <paramref name="pixels"/> must hold <see cref="CaptureFileHeader.PixelByteCount"/> bytes.</summary>
    public CaptureRecordHeader ReadRecord(long recordIndex, Span<byte> pixels)
    {
      if (pixels.Length < Header.PixelByteCount)
        throw new ArgumentException("Pixel buffer too small", nameof(pixels));
      var header = ReadRecordHeader(recordIndex);
      ReadExactly(pixels.Slice(0, Header.PixelByteCount), RecordOffset(recordIndex) + CaptureFileHeader.RecordHeaderSize);
      return header;
    }

    /// <summary>Read a record into a (reusable) image of the stored frame size.</summary>
    public CaptureRecordHeader ReadRecord(long recordIndex, GrayImage target)
    {
      if (target.Width != Header.Width || target.Height != Header.Height || target.Stride != Header.Width)
        throw new ArgumentException("Target image does not match the capture frame size", nameof(target));
      return ReadRecord(recordIndex, target.Pixels.AsSpan());
    }

    public GrayImage CreateFrameImage() => new GrayImage(Header.Width, Header.Height);

    public void Dispose() => m_handle.Dispose();

    private long RecordOffset(long recordIndex)
    {
      if (recordIndex < 0 || recordIndex >= RecordCount)
        throw new ArgumentOutOfRangeException(nameof(recordIndex));
      return CaptureFileHeader.HeaderSize + (recordIndex * Header.RecordSize);
    }

    private void ReadExactly(Span<byte> buffer, long offset)
    {
      while (!buffer.IsEmpty)
      {
        int read = RandomAccess.Read(m_handle, buffer, offset);
        if (read <= 0)
          throw new EndOfStreamException("Unexpected end of capture file");
        buffer = buffer.Slice(read);
        offset += read;
      }
    }
  }
}
