//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes captures.mbcd: the header first (rewritten by Complete once the marker locations are known), then whole records as they come. A new
//* file only: it never replaces an existing one, and a header that cannot be written leaves no file behind.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace MB.FramePacing.Data
{
  public sealed class CaptureDataWriter : IDisposable
  {
    private readonly SafeFileHandle m_handle;
    private long m_offset;
    private bool m_disposed;

    public CaptureDataWriter(string path, CaptureDataHeader header)
    {
      Header = header ?? throw new ArgumentNullException(nameof(header));
      // The header's bytes first: one that cannot be written (too many markers) throws before there is a file
      Span<byte> bytes = stackalloc byte[CaptureDataHeader.HeaderSize];
      header.Write(bytes);
      m_handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
      try
      {
        RandomAccess.Write(m_handle, bytes, 0);
      }
      catch
      {
        m_handle.Dispose();
        File.Delete(path);
        throw;
      }
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

    /// <summary>Write records, in order.</summary>
    public void WriteRecords(IReadOnlyList<CaptureDataRecord> records)
    {
      const int Batch = 4096;
      var buffer = new byte[Math.Min(Batch, Math.Max(records.Count, 1)) * CaptureDataRecord.Size];
      for (int first = 0; first < records.Count; first += Batch)
      {
        int count = Math.Min(Batch, records.Count - first);
        for (int i = 0; i < count; ++i)
          records[first + i].Write(buffer.AsSpan(i * CaptureDataRecord.Size, CaptureDataRecord.Size));
        WriteRecords(buffer.AsSpan(0, count * CaptureDataRecord.Size));
      }
    }

    /// <summary>
    /// Rewrite the header with what is known at the end of the capture (the marker locations). A header that cannot be written throws and
    /// leaves the file's and <see cref="Header"/> as they were.
    /// </summary>
    public void Complete(CaptureDataHeader header)
    {
      ObjectDisposedException.ThrowIf(m_disposed, this);
      ArgumentNullException.ThrowIfNull(header);
      Span<byte> bytes = stackalloc byte[CaptureDataHeader.HeaderSize];
      header.Write(bytes);
      RandomAccess.Write(m_handle, bytes, 0);
      Header = header;
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      m_handle.Dispose();
    }
  }
}
