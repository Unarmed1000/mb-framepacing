//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of decoding a marker: decoded, not found, or found with an invalid payload.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  public enum MarkerDecodeStatus
  {
    /// <summary>A marker with a valid payload was decoded.</summary>
    Decoded,

    /// <summary>No readable QR code (missing, torn, blended between two frames or too small).</summary>
    NotFound,

    /// <summary>A QR code was read but it is not a frame marker (wrong length, magic or format version).</summary>
    InvalidPayload,
  }
}
