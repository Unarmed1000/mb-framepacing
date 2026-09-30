//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Renderer, sizing helpers and multi-marker (tearing) detection.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerRendererTests
  {
    [Test]
    public void EveryMarkerKind_IsVersion6()
    {
      Assert.That(MarkerRenderer.GenerateModules(new MarkerPayload(MarkerKind.Frame, 3, 1, MB.FrameMarker.MarkerFlags.None, 2)).Size, Is.EqualTo(41));
      Assert.That(
        MarkerRenderer.GenerateModules(new MarkerPayload(MarkerKind.SequenceEnd, 3, 1, MB.FrameMarker.MarkerFlags.None, 2)).Size,
        Is.EqualTo(41)
      );
      var start = new MarkerPayload(MarkerKind.SequenceStart, 3, 1, MB.FrameMarker.MarkerFlags.None, 2);
      Assert.That(MarkerRenderer.GenerateModules(start).Size, Is.EqualTo(41));
      var full = MarkerRenderer.GenerateModules(
        start,
        new StartMetadata(1, new MB.FrameMarker.SequenceId(0xFEDC_BA98_7654_3210, 0x0123_4567_89AB_CDEF))
      );
      Assert.That(full.Size, Is.EqualTo(MarkerRenderer.QrModuleCount));
    }

    [Test]
    public void PacingFields_SurviveRenderingAndDecoding()
    {
      var payload = new MarkerPayload(
        MarkerKind.Frame,
        30,
        10,
        MB.FrameMarker.MarkerFlags.None,
        20,
        TargetFrameTicks: 166_667,
        IntendedDisplayTicks: 1_234_567_890_123
      );
      var image = new GrayImage(400, 400, 96);
      MarkerRenderer.Render(image, payload, 20, 20, 3, MarkerRenderer.RecommendedQuietZoneModules);
      var result = new MarkerDecoder().Decode(image);
      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Payload, Is.EqualTo(payload));
    }

    [TestCase(1080, 1080, false, 2, 3)]
    [TestCase(1080, 1080, true, 2, 4)]
    [TestCase(1440, 1080, false, 3, 4)]
    [TestCase(1080, 540, false, 4, 6)]
    [TestCase(2160, 1080, false, 4, 6)]
    [TestCase(1080, 360, false, 6, 9)]
    [TestCase(2160, 540, false, 8, 12)]
    public void SizingTable(int sourceHeight, int storedHeight, bool mjpeg, int expectedMinimum, int expectedRecommended)
    {
      Assert.That(MarkerRenderer.MinimumModuleSizePx(sourceHeight, storedHeight), Is.EqualTo(expectedMinimum));
      Assert.That(MarkerRenderer.RecommendModuleSizePx(sourceHeight, storedHeight, mjpeg), Is.EqualTo(expectedRecommended));
    }

    [Test]
    public void DecodeAll_FindsTearingMarkersTopToBottom()
    {
      // 294 px markers at the top, middle and bottom of the frame
      var image = new GrayImage(640, 1100, 128);
      MarkerRenderer.Render(image, new MarkerPayload(MarkerKind.Frame, 1, 10, MB.FrameMarker.MarkerFlags.None, 100), 32, 32, 6);
      MarkerRenderer.Render(image, new MarkerPayload(MarkerKind.Frame, 1, 10, MB.FrameMarker.MarkerFlags.None, 100), 32, 400, 6);
      MarkerRenderer.Render(image, new MarkerPayload(MarkerKind.Frame, 1, 11, MB.FrameMarker.MarkerFlags.None, 200), 32, 768, 6);

      var results = new MarkerDecoder(tryHarder: true).DecodeAll(image);

      Assert.That(results, Has.Count.EqualTo(3));
      Assert.That(results[0].Payload.FrameIndex, Is.EqualTo(10UL));
      Assert.That(results[1].Payload.FrameIndex, Is.EqualTo(10UL));
      Assert.That(results[2].Payload.FrameIndex, Is.EqualTo(11UL));
      Assert.That(results[0].Bounds.Y, Is.LessThan(results[1].Bounds.Y));
    }

    [Test]
    public void Decode_ForeignQrCode_IsInvalidPayload()
    {
      var image = new GrayImage(300, 300, 255);
      var hints = new System.Collections.Generic.Dictionary<ZXing.EncodeHintType, object>();
      var code = ZXing.QrCode.Internal.Encoder.encode("https://example.com", ZXing.QrCode.Internal.ErrorCorrectionLevel.M, hints);
      for (int y = 0; y < code.Matrix.Height; ++y)
      {
        for (int x = 0; x < code.Matrix.Width; ++x)
        {
          if (code.Matrix[x, y] == 1)
            image.FillRect(new PixelRect(40 + (x * 6), 40 + (y * 6), 6, 6), 0);
        }
      }
      Assert.That(new MarkerDecoder().Decode(image).Status, Is.EqualTo(MarkerDecodeStatus.InvalidPayload));
    }

    [Test]
    public void Decode_EmptyImage_IsNotFound()
    {
      Assert.That(new MarkerDecoder().Decode(new GrayImage(200, 200, 128)).Status, Is.EqualTo(MarkerDecodeStatus.NotFound));
    }
  }
}
