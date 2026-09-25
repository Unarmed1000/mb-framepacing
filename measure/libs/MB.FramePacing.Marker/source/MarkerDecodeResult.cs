//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of decoding one marker.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  /// <summary>The outcome of decoding one marker.</summary>
  /// <param name="Start">The start metadata when the marker is a <see cref="MarkerKind.SequenceStart"/> marker, otherwise null.</param>
  /// <param name="Bounds">Marker bounds including the quiet zone, in image pixels. Only valid when a QR code was found.</param>
  /// <param name="ModuleSizePx">Measured size of one QR module in image pixels. Only valid when a QR code was found.</param>
  /// <param name="Geometry">
  /// The finder and alignment pattern centres when the detector found all four (not for the locked fast path, which samples the grid directly).
  /// </param>
  public readonly record struct MarkerDecodeResult(
    MarkerDecodeStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start,
    PixelRect Bounds,
    float ModuleSizePx,
    MarkerGeometry? Geometry = null
  )
  {
    public static readonly MarkerDecodeResult NotFound = new MarkerDecodeResult(MarkerDecodeStatus.NotFound, default, null, default, 0);

    public bool IsDecoded => Status == MarkerDecodeStatus.Decoded;
  }
}
