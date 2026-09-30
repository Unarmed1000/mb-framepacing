//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What reading a capture's markers gave (captures.mbcd record byte 28).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  public enum CaptureDataStatus : byte
  {
    /// <summary>No marker could be read (the frame changed during capture, blended, damaged, or no marker yet).</summary>
    Undecodable = 0,

    /// <summary>The main marker was read.</summary>
    Decoded = 1,

    /// <summary>The markers at different heights of the frame disagree (the capture shows parts of two frames).</summary>
    Torn = 2,
  }
}
