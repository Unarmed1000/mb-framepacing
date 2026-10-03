//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture cannot store only the markers' regions: the marker moved while it was located, or it is not fully inside the frame. The
//* whole frame can still be captured, which is what a capture that crops by default falls back to.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.MarkerDecoding
{
  public sealed class MarkerRegionException : InvalidOperationException
  {
    public MarkerRegionException(string message)
      : base(message) { }

    public MarkerRegionException(string message, Exception innerException)
      : base(message, innerException) { }

    public MarkerRegionException() { }
  }
}
