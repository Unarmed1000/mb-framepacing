//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Bit counts of a 64-bit word. .NET Standard 2.1 (and so Unity) has no System.Numerics.BitOperations, so these are plain arithmetic.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  internal static class BitUtil
  {
    /// <summary>How many bits of <paramref name="value"/> are set.</summary>
    public static int PopCount(ulong value)
    {
      // Sums of neighbouring bits, then of pairs, then of nibbles; the multiplication adds the eight byte sums into the highest byte
      value -= (value >> 1) & 0x5555555555555555UL;
      value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
      value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
      return (int)((value * 0x0101010101010101UL) >> 56);
    }

    /// <summary>How many zero bits <paramref name="value"/> has above its highest set bit: 64 for 0.</summary>
    public static int LeadingZeroCount(ulong value)
    {
      // Every bit below the highest set one becomes set: what is left unset are the leading zeros
      value |= value >> 1;
      value |= value >> 2;
      value |= value >> 4;
      value |= value >> 8;
      value |= value >> 16;
      value |= value >> 32;
      return 64 - PopCount(value);
    }
  }
}
