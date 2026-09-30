//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the swap interval rule decided on a frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer
{
  public enum SwapIntervalChange : byte
  {
    None = 0,

    /// <summary>A longer swap interval: a lower frame rate.</summary>
    Slower = 1,

    /// <summary>A shorter swap interval: a higher frame rate.</summary>
    Faster = 2,
  }
}
