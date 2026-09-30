//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Decodes one captured frame's markers once their layout is known: the main marker, and the other locks either for the tearing check
//* (capture cards: torn when they disagree) or, for EXPERIMENTAL camera captures, the second zone's frame index (the scanout). Used live
//* during a capture (LiveFrameDecoder) and afterwards on frames.mbfc (the analysis), so both produce the same records.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  public static class FrameMarkerDecoder
  {
    /// <summary>Decode a frame with the locked <paramref name="layout"/>.</summary>
    public static FrameDecode DecodeLocked(MarkerDecoder decoder, GrayImage image, MarkerLayout layout, bool camera) =>
      camera ? DecodeCameraFrame(decoder, image, layout) : DecodeFrame(decoder, image, layout);

    /// <summary>
    /// A frame decoded by the search (before the layout is known): the first main marker is the main one, a sync marker the second one.
    /// Torn as with the locked layout: the sync marker shows another frame, or only the sync marker could be read.
    /// </summary>
    public static FrameDecode FromSearch(IReadOnlyList<MarkerDecodeResult> markers)
    {
      // MarkerDecodeResult is a struct whose default reads as decoded: look for the markers explicitly
      MarkerDecodeResult? main = null;
      MarkerDecodeResult? sync = null;
      foreach (var marker in markers)
      {
        if (!marker.IsDecoded)
          continue;
        if (marker.Payload.Kind == MarkerKind.Sync)
          sync ??= marker;
        else
          main ??= marker;
      }
      byte[]? second = sync?.Bytes;
      if (main is not { } found)
        return new FrameDecode(sync != null ? CaptureDataStatus.Torn : CaptureDataStatus.Undecodable, MarkerDecodeResult.NotFound, second);
      bool torn = sync is { } other && !other.Payload.IsSameFrame(found.Payload);
      return new FrameDecode(torn ? CaptureDataStatus.Torn : CaptureDataStatus.Decoded, found, second);
    }

    /// <summary>
    /// EXPERIMENTAL camera captures store the rig's rectified zones stacked top to bottom in scanout order, each marker at a known origin with
    /// <see cref="CameraZone.StoredPxPerModule"/> pixel modules: no search needed.
    /// </summary>
    public static MarkerLayout CameraLayout(CaptureFileHeader header)
    {
      int zones = header.Height / CameraZone.StoredSizePx;
      if (zones < 1 || header.Width != CameraZone.StoredSizePx)
        throw new InvalidOperationException(
          $"A {header.Width}x{header.Height} capture is not a camera capture (expected {CameraZone.StoredSizePx} pixel wide stacked zones)"
        );
      return MarkerLayout.For(
        Enumerable.Range(0, Math.Min(zones, CaptureDataHeader.MaxMarkers)).Select(CameraZone.StoredLock).ToList(),
        camera: true
      );
    }

    /// <summary>
    /// Camera: the zones legitimately show different frames while the scanout rolls down the screen, so they are not compared here; the
    /// timeline uses the second zone's frame index to measure the scanout and find tears.
    /// </summary>
    private static FrameDecode DecodeCameraFrame(MarkerDecoder decoder, GrayImage image, MarkerLayout layout)
    {
      var primary = decoder.DecodeLocked(image, layout.Primary);
      byte[]? second = null;
      if (layout.Locks.Count > 1 && decoder.DecodeLocked(image, layout.Locks[1]) is { IsDecoded: true } other)
        second = other.Bytes;
      return primary.IsDecoded
        ? new FrameDecode(CaptureDataStatus.Decoded, primary, second)
        : new FrameDecode(CaptureDataStatus.Undecodable, MarkerDecodeResult.NotFound, second);
    }

    private static FrameDecode DecodeFrame(MarkerDecoder decoder, GrayImage image, MarkerLayout layout)
    {
      var primary = decoder.DecodeLocked(image, layout.Primary);
      bool torn = false;
      byte[]? second = null;
      for (int i = 1; i < layout.Locks.Count; ++i)
      {
        var other = decoder.DecodeLocked(image, layout.Locks[i]);
        if (other.IsDecoded)
          second ??= other.Bytes;
        if (other.IsDecoded && primary.IsDecoded && !other.Payload.IsSameFrame(primary.Payload))
          torn = true;
        if (other.IsDecoded && !primary.IsDecoded)
          torn = true;
      }
      if (!primary.IsDecoded)
        return new FrameDecode(torn ? CaptureDataStatus.Torn : CaptureDataStatus.Undecodable, MarkerDecodeResult.NotFound, second);
      return new FrameDecode(torn ? CaptureDataStatus.Torn : CaptureDataStatus.Decoded, primary, second);
    }
  }
}
