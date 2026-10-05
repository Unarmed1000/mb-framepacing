//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of decoding a marker: decoded, not found, or found with an invalid payload.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.MarkerDecoding
{
  public enum MarkerDecodeStatus
  {
    /// <summary>A marker with a valid payload was decoded.</summary>
    Decoded,

    /// <summary>No readable QR code (missing, torn, blended between two frames or too small).</summary>
    NotFound,

    /// <summary>A QR code was read but it is not a frame marker (wrong length, magic, format version or CRC).</summary>
    InvalidPayload,
  }
}
