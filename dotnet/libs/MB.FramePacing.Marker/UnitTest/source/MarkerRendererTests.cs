//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Renderer, sizing helpers and multi-marker (tearing) detection.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerRendererTests
  {
    [Test]
    public void FrameAndEndMarkers_AreVersion2()
    {
      Assert.That(MarkerRenderer.GenerateModules(new MarkerPayload(1, 2, 3)).Size, Is.EqualTo(25));
      Assert.That(MarkerRenderer.GenerateModules(new MarkerPayload(1, 2, 3, MarkerKind.SequenceEnd)).Size, Is.EqualTo(25));
    }

    [Test]
    public void StartMarker_GrowsWithName_UpToMaxVersion()
    {
      var start = new MarkerPayload(1, 2, 3, MarkerKind.SequenceStart);
      Assert.That(MarkerRenderer.GenerateModules(start).Size, Is.EqualTo(29));
      var full = MarkerRenderer.GenerateModules(start, new StartMetadata(1, new string('x', MarkerPayload.MaxStartNameBytes)));
      Assert.That(full.Size, Is.LessThanOrEqualTo(MarkerRenderer.MaxQrModuleCount));
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
      var image = new GrayImage(640, 900, 128);
      MarkerRenderer.Render(image, new MarkerPayload(10, 100, 1), 32, 32, 6);
      MarkerRenderer.Render(image, new MarkerPayload(10, 100, 1), 32, 351, 6);
      MarkerRenderer.Render(image, new MarkerPayload(11, 200, 1), 32, 670, 6);

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
