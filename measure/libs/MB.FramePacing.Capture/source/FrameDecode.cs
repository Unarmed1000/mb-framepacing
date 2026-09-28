//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What one captured frame's markers said: the main marker (frame, start or end) and the second one (the sync marker that checks tearing,
//* or a camera's second zone that measures the scanout). The same whether decoded live during the capture or afterwards from frames.mbfc.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Data;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  /// <param name="Status">Decoded, undecodable or torn.</param>
  /// <param name="Main">The main marker's decode (<see cref="MarkerDecodeResult.NotFound"/> when there was none).</param>
  /// <param name="SecondBytes">The second marker's encoded bytes, when it was read.</param>
  public readonly record struct FrameDecode(CaptureDataStatus Status, MarkerDecodeResult Main, byte[]? SecondBytes)
  {
    public static readonly FrameDecode Undecodable = new FrameDecode(CaptureDataStatus.Undecodable, MarkerDecodeResult.NotFound, null);

    /// <summary>The main marker's encoded bytes, when it was read.</summary>
    public byte[]? MainBytes => Main.IsDecoded ? Main.Bytes : null;
  }
}
