//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* FrameMarkerDecoder: what a frame's decoded markers make of the capture. A QR code that was read but is no valid payload (its CRC does
//* not match) is no marker: the capture is undecodable, or torn when the sync marker was read, and its bytes are not stored.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class FrameMarkerDecoderTests
  {
    private static readonly MarkerPayload g_frame = new MarkerPayload(
      MarkerKind.Frame,
      7,
      1000,
      MB.FramePacing.Marker.MarkerFlags.NoFlags,
      new NanosecondTimeSpan(16_666_667)
    );

    private static MarkerDecodeResult Read(byte[] bytes)
    {
      var bounds = new PixelRect(32, 32, 294, 294);
      return MarkerPayload.TryDecode(bytes, out var payload, out var start)
        ? new MarkerDecodeResult(MarkerDecodeStatus.Decoded, payload, start, bounds, 6, null, bytes)
        : new MarkerDecodeResult(MarkerDecodeStatus.InvalidPayload, default, null, bounds, 6, null, bytes);
    }

    [Test]
    public void FromSearch_AMarkerAsDrawn_IsDecoded()
    {
      var bytes = g_frame.Encode();
      var decode = FrameMarkerDecoder.FromSearch(new[] { Read(bytes) });
      Assert.That(decode.Status, Is.EqualTo(CaptureDataStatus.Decoded));
      Assert.That(decode.Main.Payload, Is.EqualTo(g_frame));
      Assert.That(decode.MainBytes, Is.EqualTo(bytes));
    }

    [Test]
    public void FromSearch_AMarkerWhoseCrcDoesNotMatch_IsNoMarker()
    {
      // One bit of the frame index: a well-formed payload of another frame, which only the CRC gives away
      var bytes = g_frame.Encode();
      bytes[8] ^= 1;
      var read = Read(bytes);
      Assert.That(read.Status, Is.EqualTo(MarkerDecodeStatus.InvalidPayload));

      var decode = FrameMarkerDecoder.FromSearch(new[] { read });
      Assert.That(decode.Status, Is.EqualTo(CaptureDataStatus.Undecodable));
      Assert.That(decode.Main.IsDecoded, Is.False);
      Assert.That(decode.MainBytes, Is.Null, "bytes that are no marker are not stored");
      Assert.That(decode.SecondBytes, Is.Null);
    }

    [Test]
    public void FromSearch_OnlyTheSyncMarkerRead_IsTorn()
    {
      var bytes = g_frame.Encode();
      bytes[8] ^= 1;
      var sync = (g_frame with { Kind = MarkerKind.Sync }).Encode();
      Assert.That(sync, Has.Length.EqualTo(20));

      var decode = FrameMarkerDecoder.FromSearch(new[] { Read(bytes), Read(sync) });
      Assert.That(decode.Status, Is.EqualTo(CaptureDataStatus.Torn));
      Assert.That(decode.MainBytes, Is.Null);
      Assert.That(decode.SecondBytes, Is.EqualTo(sync));

      // The sync marker's own CRC wrong as well: nothing was read
      sync[19] ^= 0x80;
      Assert.That(FrameMarkerDecoder.FromSearch(new[] { Read(bytes), Read(sync) }).Status, Is.EqualTo(CaptureDataStatus.Undecodable));
    }
  }
}
