//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* SplitMix64 (Steele, Lea and Flood, 2014): 64-bit integer arithmetic only, so C# draws the same numbers as the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer.UnitTest.Simulation
{
  internal sealed class SplitMix64
  {
    private ulong m_state;

    public SplitMix64(ulong seed)
    {
      m_state = seed;
    }

    public ulong Next()
    {
      unchecked
      {
        m_state += 0x9E37_79B9_7F4A_7C15UL;
        ulong z = m_state;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
      }
    }
  }
}
