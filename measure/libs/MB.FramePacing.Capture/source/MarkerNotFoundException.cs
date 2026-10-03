//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A recording was read to its end and no marker was found in it: there is nothing to measure.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  public sealed class MarkerNotFoundException : Exception
  {
    public MarkerNotFoundException(string message)
      : base(message) { }

    public MarkerNotFoundException(string message, Exception innerException)
      : base(message, innerException) { }

    public MarkerNotFoundException() { }
  }
}
