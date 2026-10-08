//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Cross-language tests: the C++ library renders the golden markers (sdk/cpp/marker/tools/marker-render --golden), the C# decoder must read them back
//* exactly - at native size and after the capture scaling described in sdk/doc/marker-format.md "Sizing".
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
{
  [TestFixture]
  public class GoldenMarkerTests
  {
    private static IEnumerable<GoldenMarker> AllGolden() => TestData.LoadGoldenMarkers();

    [TestCaseSource(nameof(AllGolden))]
    public void DecodesNativeResolution(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      var decoder = new MarkerDecoder();

      // The locked path decodes every marker: it is what the analysis uses for every capture once it found the marker
      int size = MarkerRenderer.MarkerSizePx(golden.ModuleSizePx, golden.QuietZoneModules, golden.Payload.Kind);
      var locked = decoder.DecodeLocked(image, new MarkerLock(new PixelRect(golden.OriginX, golden.OriginY, size, size), golden.ModuleSizePx));
      Assert.That(locked.Status, Is.EqualTo(MarkerDecodeStatus.Decoded));
      Assert.That(locked.Payload, Is.EqualTo(golden.Payload));
      Assert.That(locked.Start, Is.EqualTo(golden.Start));

      // Every golden marker is found by the search too, also those whose data modules form a false finder pattern (the multi code search)
      var result = decoder.Decode(image);
      Assert.That(result.Status, Is.EqualTo(MarkerDecodeStatus.Decoded));
      Assert.That(result.Payload, Is.EqualTo(golden.Payload));
      Assert.That(result.Start, Is.EqualTo(golden.Start));
      Assert.That(result.ModuleSizePx, Is.EqualTo(golden.ModuleSizePx).Within(0.25));
    }

    [TestCaseSource(nameof(AllGolden))]
    public void BoundsCoverTheMarker(GoldenMarker golden)
    {
      var image = PgmFile.Read(golden.Path);
      var result = new MarkerDecoder().Decode(image);
      Assert.That(result.IsDecoded);

      int size = MarkerRenderer.MarkerSizePx(golden.ModuleSizePx, golden.QuietZoneModules, golden.Payload.Kind);
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
      // Non-integer and integer downscales, down to the recommended 3 stored pixels per module
      foreach (double scale in new[] { 0.9, 0.8, 0.75, 0.7, 0.65, 0.6, 0.55, 0.5 })
      {
        if (golden.ModuleSizePx * scale < 3.0)
          continue;
        var scaled = image.ResizeBilinear((int)Math.Round(image.Width * scale), (int)Math.Round(image.Height * scale));
        var result = new MarkerDecoder().Decode(scaled);
        Assert.That(result.Payload, Is.EqualTo(golden.Payload), $"scale {scale}, {golden.ModuleSizePx * scale:0.##} stored px/module");
        // Where it was found, also when only the upscaled retry found it: the golden markers sit at (36, 36), quiet zone included
        double module = golden.ModuleSizePx * scale;
        Assert.That(result.ModuleSizePx, Is.EqualTo(module).Within(10).Percent, $"scale {scale}: module size");
        Assert.That(result.Bounds.X, Is.EqualTo(36 * scale).Within(module), $"scale {scale}: left edge");
        Assert.That(result.Bounds.Y, Is.EqualTo(36 * scale).Within(module), $"scale {scale}: top edge");
      }
    }

    [Test]
    public void CSharpRendererMatchesCppModules()
    {
      // Both sides use a QR encoder with automatic masking, so the module patterns can differ; what must agree is the decoded content.
      var frameLock = new MarkerLock(new PixelRect(32, 32, MarkerRenderer.MarkerSizePx(6), MarkerRenderer.MarkerSizePx(6)), 6);
      foreach (var golden in TestData.LoadGoldenMarkers().Where(g => g.ModuleSizePx == 6))
      {
        var payload = golden.Payload;
        var image = new GrayImage(400, 400, 128);
        MarkerRenderer.Render(image, payload, 32, 32, 6, metadata: golden.Start);
        var result = new MarkerDecoder().DecodeLocked(image, frameLock);
        Assert.That(result.Payload, Is.EqualTo(payload), golden.ToString());
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
        var payload = new MarkerPayload(
          MarkerKind.Frame,
          (uint)random.Next(),
          (ulong)random.NextInt64(),
          MB.FramePacing.Marker.MarkerFlags.NoFlags,
          // Any time, to the nanosecond
          new NanosecondTimeSpan(random.NextInt64())
        );
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
      var start = new StartMetadata(639_257_616_000_000_000, new MB.FramePacing.Marker.SequenceId(0xFEDC_BA98_7654_3210, 0x0123_4567_89AB_CDEF));
      var payload = new MarkerPayload(MarkerKind.SequenceStart, 7, 5, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(600));
      var image = new GrayImage(400, 400, 128);
      MarkerRenderer.Render(image, payload, 32, 32, 6, metadata: start);

      var result = new MarkerDecoder().DecodeLocked(image, frameLock);

      Assert.That(result.Payload, Is.EqualTo(payload));
      Assert.That(result.Start, Is.EqualTo(start));
    }
  }
}
