//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a capture's marker is: its bounds (including the quiet zone) and its module size, in stored pixels.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  public readonly record struct MarkerLocation(DataRect Bounds, double ModuleSizePx);
}
