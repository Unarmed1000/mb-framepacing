//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads the fields of one protobuf message from its bytes, one after the other: what the trace processor's replies are decoded with
//* (ProtoWriter has the wire format). Bytes that are not a message are an InvalidDataException.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.IO;

namespace MB.FramePacing.Trace
{
  internal ref struct ProtoReader
  {
    public const int WireVarint = 0;
    public const int WireFixed64 = 1;
    public const int WireLength = 2;
    public const int WireFixed32 = 5;

    private ReadOnlySpan<byte> m_rest;

    public ProtoReader(ReadOnlySpan<byte> message)
    {
      m_rest = message;
      Field = 0;
      WireType = 0;
    }

    /// <summary>The number of the field <see cref="Next"/> stopped at.</summary>
    public int Field { get; private set; }

    public int WireType { get; private set; }

    /// <summary>True when the bytes are used up.</summary>
    public readonly bool AtEnd => m_rest.IsEmpty;

    /// <summary>Go to the next field. False at the message's end. The field's value has to be read or skipped before the next call.</summary>
    public bool Next()
    {
      if (m_rest.IsEmpty)
        return false;
      ulong tag = Varint();
      Field = checked((int)(tag >> 3));
      WireType = (int)(tag & 7);
      return true;
    }

    public ulong Varint()
    {
      ulong value = 0;
      for (int shift = 0; shift < 64; shift += 7)
      {
        if (m_rest.IsEmpty)
          throw new InvalidDataException("A protobuf number ends before its last byte");
        byte part = m_rest[0];
        m_rest = m_rest[1..];
        value |= (ulong)(part & 0x7F) << shift;
        if ((part & 0x80) == 0)
          return value;
      }
      throw new InvalidDataException("A protobuf number is longer than ten bytes");
    }

    public ulong Fixed64()
    {
      if (m_rest.Length < 8)
        throw new InvalidDataException("A protobuf message ends inside a field of eight bytes");
      ulong value = BinaryPrimitives.ReadUInt64LittleEndian(m_rest);
      m_rest = m_rest[8..];
      return value;
    }

    /// <summary>The bytes of a field with a length: a text, bytes, a message, or packed numbers.</summary>
    public ReadOnlySpan<byte> Bytes()
    {
      ulong length = Varint();
      if (length > (ulong)m_rest.Length)
        throw new InvalidDataException("A protobuf field is longer than the message it is in");
      var bytes = m_rest[..(int)length];
      m_rest = m_rest[(int)length..];
      return bytes;
    }

    /// <summary>Pass over the value of the field <see cref="Next"/> stopped at.</summary>
    public void Skip()
    {
      switch (WireType)
      {
        case WireVarint:
          Varint();
          break;
        case WireFixed64:
          Fixed64();
          break;
        case WireLength:
          Bytes();
          break;
        case WireFixed32:
          if (m_rest.Length < 4)
            throw new InvalidDataException("A protobuf message ends inside a field of four bytes");
          m_rest = m_rest[4..];
          break;
        default:
          throw new InvalidDataException($"A protobuf field has the wire type {WireType}, which is not read");
      }
    }
  }
}
