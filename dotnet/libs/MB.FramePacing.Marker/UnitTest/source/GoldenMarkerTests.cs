//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Cross-language tests: the C++ library renders the golden markers (cpp/tools/marker-render --golden), the C# decoder must read them back
//* exactly - at native size and after the capture scaling described in doc/marker-format.md "Sizing".
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class GoldenMarkerTests
  {
    private static IEnumerable<GoldenMarker> AllGolden() => TestData.LoadGoldenMarkers();

    [TestCaseSource(nameof(AllGolden))]
    public void DecodesNativeResolution(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      var result = new MarkerDecoder().Decode(image);

      Assert.That(result.Status, Is.EqualTo(MarkerDecodeStatus.Decoded));
      Assert.That(result.Payload, Is.EqualTo(golden.Payload));
      Assert.That(result.Start, Is.EqualTo(golden.Start));
      Assert.That(result.ModuleSizePx, Is.EqualTo(golden.ModuleSizePx).Within(0.25f));
    }

    [TestCaseSource(nameof(AllGolden))]
    public void BoundsCoverTheMarker(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      var result = new MarkerDecoder().Decode(image);
      Assume.That(result.IsDecoded);

      int moduleCount = golden.Payload.Kind == MarkerKind.SequenceStart ? SymbolSize(golden) : MarkerRenderer.FrameQrModuleCount;
      int size = MarkerRenderer.MarkerSizePx(golden.ModuleSizePx, golden.QuietZoneModules, moduleCount);
      var expected = new PixelRect(golden.OriginX, golden.OriginY, size, size);
      // The estimate is built from the finder centres; allow one module of slack on each side
      int slack = golden.ModuleSizePx + 1;
      Assert.That(Math.Abs(result.Bounds.X - expected.X), Is.LessThanOrEqualTo(slack));
      Assert.That(Math.Abs(result.Bounds.Y - expected.Y), Is.LessThanOrEqualTo(slack));
      Assert.That(Math.Abs(result.Bounds.Right - expected.Right), Is.LessThanOrEqualTo(slack));
      Assert.That(Math.Abs(result.Bounds.Bottom - expected.Bottom), Is.LessThanOrEqualTo(slack));

      // Decoding again inside the reported bounds must work (that is how the analyzer locks onto the marker)
      Assert.That(new MarkerDecoder().Decode(image, result.Bounds.Inflate(golden.ModuleSizePx)).Payload, Is.EqualTo(golden.Payload));
    }

    /// <summary>
    /// The "Sizing" table: with an integer (area averaging) downscale every marker with at least 2 stored pixels per module must decode.
    /// </summary>
    [TestCaseSource(nameof(AllGolden))]
    public void DecodesAfterIntegerDownscale(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      foreach (int factor in new[] { 2, 3, 4, 6 })
      {
        if (golden.ModuleSizePx % factor != 0 || golden.ModuleSizePx / factor < 2)
          continue;
        var scaled = image.DownscaleBox(factor);
        var result = new MarkerDecoder().Decode(scaled);
        Assert.That(result.Payload, Is.EqualTo(golden.Payload), $"factor {factor}, {golden.ModuleSizePx / factor} stored px/module");
      }
    }

    /// <summary>Non-integer scaling (for example 1440p -> 1080p, s = 0.75) at the recommended size of 3+ stored pixels per module.</summary>
    [TestCaseSource(nameof(AllGolden))]
    public void DecodesAfterBilinearScaleAtRecommendedSize(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      foreach (double scale in new[] { 0.75, 0.6, 0.5 })
      {
        if (golden.ModuleSizePx * scale < 3.0)
          continue;
        var scaled = image.ResizeBilinear((int)Math.Round(image.Width * scale), (int)Math.Round(image.Height * scale));
        var result = new MarkerDecoder().Decode(scaled);
        Assert.That(result.Payload, Is.EqualTo(golden.Payload), $"scale {scale}, {golden.ModuleSizePx * scale:0.##} stored px/module");
      }
    }

    [Test]
    public void CSharpRendererMatchesCppModules()
    {
      // Both sides use a QR encoder with automatic masking, so the module patterns can differ; what must agree is the decoded content.
      var frameLock = new MarkerLock(new PixelRect(32, 32, MarkerRenderer.MarkerSizePx(6), MarkerRenderer.MarkerSizePx(6)), 6);
      foreach (var golden in TestData.LoadGoldenMarkers().Where(g => g.ModuleSizePx == 6))
      {
        var image = new GrayImage(400, 400, 128);
        MarkerRenderer.Render(image, golden.Payload, 32, 32, 6, metadata: golden.Start);
        var result = new MarkerDecoder().DecodeLocked(image, frameLock);
        Assert.That(result.Payload, Is.EqualTo(golden.Payload), golden.ToString());
        Assert.That(result.Start, Is.EqualTo(golden.Start), golden.ToString());
      }
    }

    /// <summary>Locked decoding must read every payload, including the rare module patterns that defeat the finder pattern detector.</summary>
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    [TestCase(8)]
    public void DecodeLocked_RandomPayloads(int moduleSize)
    {
      var random = new Random(moduleSize);
      int size = MarkerRenderer.MarkerSizePx(moduleSize);
      var decoder = new MarkerDecoder();
      for (int i = 0; i < 400; ++i)
      {
        var payload = new MarkerPayload((ulong)random.NextInt64(), random.NextInt64(), (uint)random.Next());
        var image = new GrayImage(size + 80, size + 80, 128);
        MarkerRenderer.Render(image, payload, 32, 32, moduleSize);
        // A lock estimated from an earlier frame is off by up to one module
        int jitter = random.Next(-moduleSize, moduleSize + 1);
        var markerLock = new MarkerLock(new PixelRect(32 + jitter, 32 - jitter, size, size), moduleSize);
        Assert.That(decoder.DecodeLocked(image, markerLock).Payload, Is.EqualTo(payload), $"payload {i}");
      }
    }

    [Test]
    public void DecodeLocked_FindsStartMarkerDrawnAtTheSameOrigin()
    {
      var frameLock = new MarkerLock(new PixelRect(32, 32, MarkerRenderer.MarkerSizePx(6), MarkerRenderer.MarkerSizePx(6)), 6);
      var start = new StartMetadata(639_257_616_000_000_000, new string('n', MarkerPayload.MaxStartNameBytes));
      var payload = new MarkerPayload(5, 6, 7, MarkerKind.SequenceStart);
      var image = new GrayImage(400, 400, 128);
      MarkerRenderer.Render(image, payload, 32, 32, 6, metadata: start);

      var result = new MarkerDecoder().DecodeLocked(image, frameLock);

      Assert.That(result.Payload, Is.EqualTo(payload));
      Assert.That(result.Start, Is.EqualTo(start));
    }

    private static int SymbolSize(GoldenMarker golden) => MarkerRenderer.GenerateModules(golden.Payload, golden.Start).Size;
  }
}
