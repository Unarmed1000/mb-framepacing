//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a capture's marker is: its bounds (including the quiet zone) and its module size, in stored pixels.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  public readonly record struct MarkerLocation(DataRect Bounds, double ModuleSizePx);
}
