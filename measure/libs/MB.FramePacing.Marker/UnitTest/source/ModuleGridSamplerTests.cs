//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The module grid decoder camera captures use (DecodeGrid / sampleModuleGrid): frame and start markers of every version, soft edges, and
//* that capture card decoders keep the pure barcode path.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class ModuleGridSamplerTests
  {
    private const int Origin = 8;
    private const int ModulePx = 4;

    private static MarkerLock Lock() =>
      new MarkerLock(new PixelRect(Origin, Origin, MarkerRenderer.MarkerSizePx(ModulePx), MarkerRenderer.MarkerSizePx(ModulePx)), ModulePx);

    [Test]
    public void DecodeGrid_FrameMarker()
    {
      var image = new GrayImage(240, 240, 96);
      var payload = new MarkerPayload(4242, 123456, 7);
      MarkerRenderer.Render(image, payload, Origin, Origin, ModulePx);

      var result = new MarkerDecoder().DecodeGrid(image, Lock());

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Payload, Is.EqualTo(payload));
    }

    [TestCase(0)]
    [TestCase(8)]
    [TestCase(30)]
    [TestCase(MarkerPayload.MaxStartNameBytes)]
    public void DecodeGrid_StartMarkersOfEveryVersion(int nameLength)
    {
      var image = new GrayImage(240, 240, 96);
      var payload = new MarkerPayload(1, 0, 3, MarkerKind.SequenceStart);
      var start = new StartMetadata(638000000000000000, new string('n', nameLength));
      MarkerRenderer.Render(image, payload, Origin, Origin, ModulePx, MarkerRenderer.RecommendedQuietZoneModules, start);

      var result = new MarkerDecoder().DecodeGrid(image, Lock());

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Start!.Name, Is.EqualTo(start.Name));
    }

    [Test]
    public void DecodeGrid_SoftEdges()
    {
      // Area downscale then bilinear upscale: every edge is spread over two pixels, like a rectified camera zone
      var sharp = new GrayImage(240, 240, 96);
      var payload = new MarkerPayload(99, 1, 2);
      MarkerRenderer.Render(sharp, payload, Origin, Origin, ModulePx);
      var soft = sharp.DownscaleBox(2).ResizeBilinear(240, 240);

      var result = new MarkerDecoder(sampleModuleGrid: true).DecodeLocked(soft, Lock());

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Payload, Is.EqualTo(payload));
    }

    [Test]
    public void DecodeGrid_NothingThere()
    {
      Assert.That(new MarkerDecoder().DecodeGrid(new GrayImage(240, 240, 128), Lock()).IsDecoded, Is.False);
    }
  }
}
