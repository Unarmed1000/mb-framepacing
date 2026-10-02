//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The module grid decoder camera captures use (DecodeGrid / sampleModuleGrid): frame and start markers of every version, soft edges, and
//* that capture card decoders keep the pure barcode path.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
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
      var payload = new MarkerPayload(MarkerKind.Frame, 7, 4242, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(123456));
      MarkerRenderer.Render(image, payload, Origin, Origin, ModulePx);

      var result = new MarkerDecoder().DecodeGrid(image, Lock());

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Payload, Is.EqualTo(payload));
    }

    [TestCase(0ul, 0ul)]
    [TestCase(0x7465_7374_0000_0000ul, 0ul)]
    [TestCase(0xFFFF_FFFF_FFFF_FFFFul, 0xFFFF_FFFF_FFFF_FFFFul)]
    [TestCase(0xFEDC_BA98_7654_3210ul, 0x0123_4567_89AB_CDEFul)]
    public void DecodeGrid_StartMarkers(ulong high, ulong low)
    {
      var image = new GrayImage(240, 240, 96);
      var payload = new MarkerPayload(MarkerKind.SequenceStart, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(0));
      var start = new StartMetadata(638000000000000000, new MB.FramePacing.Marker.SequenceId(high, low));
      MarkerRenderer.Render(image, payload, Origin, Origin, ModulePx, MarkerRenderer.RecommendedQuietZoneModules, start);

      var result = new MarkerDecoder().DecodeGrid(image, Lock());

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Start, Is.EqualTo(start));
    }

    [Test]
    public void DecodeGrid_SoftEdges()
    {
      // Area downscale then bilinear upscale: every edge is spread over two pixels, like a rectified camera zone
      var sharp = new GrayImage(240, 240, 96);
      var payload = new MarkerPayload(MarkerKind.Frame, 2, 99, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(1));
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
