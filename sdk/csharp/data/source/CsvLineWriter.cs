//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Builds one line of an analysis CSV in a buffer it keeps, and hands it to the writer whole: no string per cell and none per line. A
//* number is written as its digits with a '-' in front when negative (the invariant culture's text), bytes as upper case hexadecimal.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;

namespace MB.FramePacing.Data
{
  internal sealed class CsvLineWriter
  {
    // The longest whole number: -9223372036854775808 and 18446744073709551615 are 20 characters
    private const int NumberLength = 20;

    private char[] m_buffer = new char[512];
    private int m_length;
    private bool m_hasCell;

    /// <summary>Start the next cell: empty until something is appended to it.</summary>
    public void Cell()
    {
      if (m_hasCell)
        Append(',');
      m_hasCell = true;
    }

    /// <summary>A cell with a number.</summary>
    public void Add(long value)
    {
      Cell();
      Append(value);
    }

    /// <summary>A cell with a number, empty when there is none.</summary>
    public void Add(long? value)
    {
      Cell();
      if (value is { } known)
        Append(known);
    }

    /// <summary>A cell with a number that is never negative.</summary>
    public void Add(ulong value)
    {
      Cell();
      Append(value);
    }

    /// <summary>A cell with a number that is never negative, empty when there is none.</summary>
    public void Add(ulong? value)
    {
      Cell();
      if (value is { } known)
        Append(known);
    }

    /// <summary>A cell with text, as it is; empty when there is none.</summary>
    public void Add(string? text)
    {
      Cell();
      if (text != null)
        Append(text);
    }

    /// <summary>A cell with bytes as hexadecimal, two upper case digits each; empty when there are none.</summary>
    public void AddHex(byte[]? bytes)
    {
      Cell();
      if (bytes == null || bytes.Length == 0)
        return;
      Span<char> destination = Reserve(bytes.Length * 2);
      if (!Convert.TryToHexString(bytes, destination, out int written))
        throw new InvalidOperationException("The bytes do not fit the line's buffer");
      m_length += written;
    }

    /// <summary>More of the cell in progress.</summary>
    public void Append(long value)
    {
      if (!value.TryFormat(Reserve(NumberLength), out int written, default, CultureInfo.InvariantCulture))
        throw new InvalidOperationException("The number does not fit the line's buffer");
      m_length += written;
    }

    public void Append(ulong value)
    {
      if (!value.TryFormat(Reserve(NumberLength), out int written, default, CultureInfo.InvariantCulture))
        throw new InvalidOperationException("The number does not fit the line's buffer");
      m_length += written;
    }

    public void Append(char character)
    {
      Reserve(1)[0] = character;
      ++m_length;
    }

    public void Append(string text)
    {
      text.CopyTo(Reserve(text.Length));
      m_length += text.Length;
    }

    /// <summary>Write the line and its end to <paramref name="writer"/>, and start the next one.</summary>
    public void End(TextWriter writer)
    {
      writer.WriteLine(m_buffer.AsSpan(0, m_length));
      m_length = 0;
      m_hasCell = false;
    }

    /// <summary>Room for <paramref name="count"/> more characters at the line's end.</summary>
    private Span<char> Reserve(int count)
    {
      if (m_length + count > m_buffer.Length)
        Array.Resize(ref m_buffer, Math.Max(m_buffer.Length * 2, m_length + count));
      return m_buffer.AsSpan(m_length, count);
    }
  }
}
