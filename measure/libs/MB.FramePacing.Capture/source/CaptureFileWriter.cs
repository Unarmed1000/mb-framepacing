//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Appends records to a .mbfc file with large positional writes (RandomAccess) and optional preallocation. Not thread safe: owned by the
//* recorder's writer thread.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace MB.FramePacing.Capture
{
  public sealed class CaptureFileWriter : IDisposable
  {
    private readonly SafeFileHandle m_handle;
    private long m_offset;
    private bool m_disposed;

    /// <param name="expectedRecordCount">Preallocate space for this many records (0 = no preallocation). The file is trimmed on dispose.</param>
    public CaptureFileWriter(string path, CaptureFileHeader header, long expectedRecordCount = 0)
    {
      Header = header ?? throw new ArgumentNullException(nameof(header));
      long preallocation = expectedRecordCount > 0 ? CaptureFileHeader.HeaderSize + (expectedRecordCount * header.RecordSize) : 0;
      m_handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, FileOptions.None, preallocation);

      Span<byte> headerBytes = stackalloc byte[CaptureFileHeader.HeaderSize];
      header.Write(headerBytes);
      RandomAccess.Write(m_handle, headerBytes, 0);
      m_offset = CaptureFileHeader.HeaderSize;
    }

    public CaptureFileHeader Header { get; }

    public long RecordsWritten { get; private set; }

    public long BytesWritten => m_offset;

    /// <summary>Write one or more complete, contiguous records (each <see cref="CaptureFileHeader.RecordSize"/> bytes).</summary>
    public void WriteRecords(ReadOnlySpan<byte> records)
    {
      ObjectDisposedException.ThrowIf(m_disposed, this);
      if (records.Length % Header.RecordSize != 0)
        throw new ArgumentException("The buffer must hold whole records", nameof(records));
      RandomAccess.Write(m_handle, records, m_offset);
      m_offset += records.Length;
      RecordsWritten += records.Length / Header.RecordSize;
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      // Drop any unused preallocated space so the record count follows from the file length
      RandomAccess.SetLength(m_handle, m_offset);
      m_handle.Dispose();
    }
  }
}
