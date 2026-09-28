//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Appends records to captures.mbcd with positional writes. The header is written first with what is known then (no markers located yet)
//* and rewritten by Complete once the layout is known. Not thread safe: owned by the recorder's writer thread (and Complete by its owner).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace MB.FramePacing.Capture
{
  public sealed class CaptureDataWriter : IDisposable
  {
    private readonly SafeFileHandle m_handle;
    private long m_offset;
    private bool m_disposed;

    public CaptureDataWriter(string path, CaptureDataHeader header)
    {
      Header = header ?? throw new ArgumentNullException(nameof(header));
      m_handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
      WriteHeader(header);
      m_offset = CaptureDataHeader.HeaderSize;
    }

    public CaptureDataHeader Header { get; private set; }

    public long RecordsWritten { get; private set; }

    public long BytesWritten => m_offset;

    /// <summary>Write one or more complete, contiguous records (each <see cref="CaptureDataRecord.Size"/> bytes).</summary>
    public void WriteRecords(ReadOnlySpan<byte> records)
    {
      ObjectDisposedException.ThrowIf(m_disposed, this);
      if (records.Length % CaptureDataRecord.Size != 0)
        throw new ArgumentException("The buffer must hold whole records", nameof(records));
      RandomAccess.Write(m_handle, records, m_offset);
      m_offset += records.Length;
      RecordsWritten += records.Length / CaptureDataRecord.Size;
    }

    /// <summary>Rewrite the header with what is known at the end of the capture (the marker layout).</summary>
    public void Complete(CaptureDataHeader header)
    {
      ObjectDisposedException.ThrowIf(m_disposed, this);
      Header = header ?? throw new ArgumentNullException(nameof(header));
      WriteHeader(header);
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      m_handle.Dispose();
    }

    private void WriteHeader(CaptureDataHeader header)
    {
      Span<byte> bytes = stackalloc byte[CaptureDataHeader.HeaderSize];
      header.Write(bytes);
      RandomAccess.Write(m_handle, bytes, 0);
    }
  }
}
