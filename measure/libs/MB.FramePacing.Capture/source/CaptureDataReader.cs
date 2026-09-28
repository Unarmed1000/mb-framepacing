//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads captures.mbcd: its header and its fixed size records, one at a time or all of them in large sequential reads.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace MB.FramePacing.Capture
{
  public sealed class CaptureDataReader : IDisposable
  {
    // Records per sequential read in ReadAll
    private const int Batch = 4096;

    private readonly SafeFileHandle m_handle;

    public CaptureDataReader(string path)
    {
      m_handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
      try
      {
        Span<byte> headerBytes = stackalloc byte[CaptureDataHeader.HeaderSize];
        if (RandomAccess.Read(m_handle, headerBytes, 0) != CaptureDataHeader.HeaderSize)
          throw new InvalidDataException($"'{path}' is too short to be a capture data file");
        Header = CaptureDataHeader.Read(headerBytes);
        // A capture that was killed mid-write may end with a partial record; it is ignored
        RecordCount = (RandomAccess.GetLength(m_handle) - CaptureDataHeader.HeaderSize) / CaptureDataRecord.Size;
      }
      catch
      {
        m_handle.Dispose();
        throw;
      }
    }

    public CaptureDataHeader Header { get; }

    public long RecordCount { get; }

    public CaptureDataRecord ReadRecord(long recordIndex)
    {
      if (recordIndex < 0 || recordIndex >= RecordCount)
        throw new ArgumentOutOfRangeException(nameof(recordIndex));
      Span<byte> buffer = stackalloc byte[CaptureDataRecord.Size];
      ReadExactly(buffer, CaptureDataHeader.HeaderSize + (recordIndex * CaptureDataRecord.Size));
      return CaptureDataRecord.Read(buffer);
    }

    /// <summary>Every record, in file order.</summary>
    public CaptureDataRecord[] ReadAll()
    {
      var records = new CaptureDataRecord[RecordCount];
      var buffer = new byte[Batch * CaptureDataRecord.Size];
      for (long first = 0; first < RecordCount; first += Batch)
      {
        int count = (int)Math.Min(Batch, RecordCount - first);
        var span = buffer.AsSpan(0, count * CaptureDataRecord.Size);
        ReadExactly(span, CaptureDataHeader.HeaderSize + (first * CaptureDataRecord.Size));
        for (int i = 0; i < count; ++i)
          records[first + i] = CaptureDataRecord.Read(span.Slice(i * CaptureDataRecord.Size, CaptureDataRecord.Size));
      }
      return records;
    }

    public void Dispose() => m_handle.Dispose();

    private void ReadExactly(Span<byte> buffer, long offset)
    {
      while (!buffer.IsEmpty)
      {
        int read = RandomAccess.Read(m_handle, buffer, offset);
        if (read <= 0)
          throw new EndOfStreamException("Unexpected end of capture data file");
        buffer = buffer.Slice(read);
        offset += read;
      }
    }
  }
}
