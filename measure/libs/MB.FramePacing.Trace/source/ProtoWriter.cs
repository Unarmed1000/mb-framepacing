//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes the fields of one protobuf message into a buffer it keeps: the few of Perfetto's messages the tools write are written by hand,
//* field by field in the order of their numbers (as Perfetto's own Python package serialises them), so no protobuf library is needed.
//* Clear() starts the next message in the same buffer.
//*
//* The wire format: a field is its number and wire type as a varint (number * 8 + type), then a varint (type 0), eight bytes little
//* endian (type 1), or a length as a varint and that many bytes (type 2: text, bytes, a message).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Text;

namespace MB.FramePacing.Trace
{
  internal sealed class ProtoWriter
  {
    private const int WireVarint = 0;
    private const int WireFixed64 = 1;
    private const int WireLength = 2;

    private byte[] m_buffer = new byte[256];
    private int m_length;

    /// <summary>The message so far.</summary>
    public ReadOnlySpan<byte> Bytes => m_buffer.AsSpan(0, m_length);

    public void Clear() => m_length = 0;

    /// <summary>A whole number that is not negative (uint32, uint64, an enum's value, a bool as 0 or 1).</summary>
    public void Varint(int field, ulong value)
    {
      Tag(field, WireVarint);
      Raw(value);
    }

    /// <summary>A whole number that may be negative (int32, int64): a negative one is its 64 bits, ten bytes.</summary>
    public void Signed(int field, long value) => Varint(field, unchecked((ulong)value));

    public void Fixed64(int field, ulong value)
    {
      Tag(field, WireFixed64);
      BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
      m_length += 8;
    }

    public void Double(int field, double value) => Fixed64(field, BitConverter.DoubleToUInt64Bits(value));

    public void Text(int field, string text)
    {
      Tag(field, WireLength);
      int count = Encoding.UTF8.GetByteCount(text);
      Raw((ulong)count);
      Encoding.UTF8.GetBytes(text, Reserve(count));
      m_length += count;
    }

    /// <summary>A message that was written into another writer.</summary>
    public void Message(int field, ProtoWriter message)
    {
      Tag(field, WireLength);
      Raw((ulong)message.m_length);
      message.Bytes.CopyTo(Reserve(message.m_length));
      m_length += message.m_length;
    }

    private void Tag(int field, int wireType) => Raw(((ulong)field << 3) | (uint)wireType);

    /// <summary>A varint: seven bits a byte, the low ones first, the top bit set on every byte but the last.</summary>
    private void Raw(ulong value)
    {
      var target = Reserve(10);
      int written = 0;
      while (value >= 0x80)
      {
        target[written++] = (byte)(value | 0x80);
        value >>= 7;
      }
      target[written++] = (byte)value;
      m_length += written;
    }

    private Span<byte> Reserve(int count)
    {
      if (m_length + count > m_buffer.Length)
        Array.Resize(ref m_buffer, Math.Max(m_buffer.Length * 2, m_length + count));
      return m_buffer.AsSpan(m_length, count);
    }
  }
}
