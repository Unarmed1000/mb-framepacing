//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What stops a capture and a Perfetto trace from being joined: the two do not fit together, the trace could not be read, or it is not a
//* file tracks can be added to. The message says what to do.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>A capture and a Perfetto trace that can not be joined; the message says why and what to do.</summary>
  public sealed class TraceException : Exception
  {
    public TraceException(string message)
      : base(message) { }

    public TraceException(string message, Exception innerException)
      : base(message, innerException) { }
  }
}
