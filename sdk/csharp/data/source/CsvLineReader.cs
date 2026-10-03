//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a text line by line without a string per line: each line is a span of the reader's own buffer, good until the next one is
//* read. Lines end as TextReader.ReadLine ends them: at a line feed, a carriage return, or a carriage return and a line feed; the last
//* line needs no ending, and an ending at the end of the text starts no further line.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MB.FramePacing.Data
{
  internal sealed class CsvLineReader : IDisposable
  {
    private readonly TextReader m_reader;
    private char[] m_buffer;
    private int m_start;
    private int m_end;
    private bool m_ended;

    public CsvLineReader(TextReader reader, int bufferLength = 1 << 15)
    {
      m_reader = reader ?? throw new ArgumentNullException(nameof(reader));
      m_buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, bufferLength));
    }

    /// <summary>The next line, without its ending; false at the end of the text. The span is good until the next call.</summary>
    public bool TryReadLine(out ReadOnlySpan<char> line)
    {
      int scanned = 0;
      while (true)
      {
        int found = m_buffer.AsSpan(m_start + scanned, m_end - m_start - scanned).IndexOfAny('\r', '\n');
        if (found >= 0)
        {
          int length = scanned + found;
          int ending = m_start + length;
          if (m_buffer[ending] == '\r' && ending + 1 == m_end && !m_ended)
          {
            // Whether a line feed follows the carriage return is in the text not read yet
            scanned = length;
            Fill();
            continue;
          }
          line = m_buffer.AsSpan(m_start, length);
          m_start = ending + (m_buffer[ending] == '\r' && ending + 1 < m_end && m_buffer[ending + 1] == '\n' ? 2 : 1);
          return true;
        }
        scanned = m_end - m_start;
        if (m_ended)
        {
          line = m_buffer.AsSpan(m_start, scanned);
          m_start = m_end;
          return scanned > 0;
        }
        Fill();
      }
    }

    /// <summary>The first line as text (a file's header: its column names are kept); false when the text is empty.</summary>
    public bool TryReadHeader([NotNullWhen(true)] out string? header)
    {
      bool read = TryReadLine(out var line);
      header = read ? line.ToString() : null;
      return read;
    }

    public void Dispose()
    {
      if (m_buffer.Length > 0)
        ArrayPool<char>.Shared.Return(m_buffer);
      m_buffer = Array.Empty<char>();
      m_start = 0;
      m_end = 0;
      m_ended = true;
    }

    /// <summary>Read more of the text behind what is waiting, which moves to the buffer's start (into a larger buffer when it is full).</summary>
    private void Fill()
    {
      int waiting = m_end - m_start;
      if (waiting == m_buffer.Length)
      {
        var larger = ArrayPool<char>.Shared.Rent(m_buffer.Length * 2);
        m_buffer.AsSpan(m_start, waiting).CopyTo(larger);
        ArrayPool<char>.Shared.Return(m_buffer);
        m_buffer = larger;
      }
      else if (m_start > 0)
        m_buffer.AsSpan(m_start, waiting).CopyTo(m_buffer);
      m_start = 0;
      m_end = waiting;
      int read = m_reader.Read(m_buffer.AsSpan(m_end));
      if (read == 0)
        m_ended = true;
      m_end += read;
    }
  }
}
