//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stretch of holds with one target and one preferred frame time: the reference lines are drawn per stretch, so they cost per change, not
//* per frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Charts
{
  /// <param name="Start">The first frame whose hold the stretch covers.</param>
  /// <param name="End">The frame after the last one: the stretch ends where it is first seen.</param>
  /// <param name="TargetFrameTime">The target frame time over the stretch; null on demand.</param>
  /// <param name="PreferredFrameTime">The preferred frame time over the stretch; null on demand.</param>
  public readonly record struct ReferenceStretch(int Start, int End, TimeSpan? TargetFrameTime, TimeSpan? PreferredFrameTime);
}
