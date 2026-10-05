//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Internal to the marker module: the CRC-32 every payload ends with (doc/marker-format.md; the C++ library's Crc32). It is the CRC-32 of
//* zlib, PNG and Ethernet: the polynomial 0x04C11DB7 bit reversed (0xEDB88320), the register started at and XORed at the end with
//* 0xFFFFFFFF. Half a byte per step, so the table is 16 entries: a payload is at most 81 bytes, once a frame. .NET Standard 2.1 (and so
//* Unity) has no System.IO.Hashing.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal static class Crc32
  {
    // The polynomial, bit reversed: the register shifts right
    private const uint ReversedPolynomial = 0xEDB88320u;

    private static readonly uint[] g_table = MakeTable();

    /// <summary>The CRC-32 of <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
      uint[] table = g_table;
      uint crc = 0xFFFFFFFFu;
      for (int i = 0; i < data.Length; ++i)
      {
        crc ^= data[i];
        crc = (crc >> 4) ^ table[(int)(crc & 15u)];
        crc = (crc >> 4) ^ table[(int)(crc & 15u)];
      }
      return crc ^ 0xFFFFFFFFu;
    }

    // What four shifts make of each half byte
    private static uint[] MakeTable()
    {
      var table = new uint[16];
      for (uint i = 0; i < 16u; ++i)
      {
        uint value = i;
        for (int bit = 0; bit < 4; ++bit)
          value = (value >> 1) ^ ((value & 1u) * ReversedPolynomial);
        table[i] = value;
      }
      return table;
    }
  }
}
