//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A layer of a process in a trace's frame timeline, and how many frames it has.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A frame timeline layer of a process.</summary>
  public sealed record TraceLayerInfo(string Process, string Layer, long Frames);
}
