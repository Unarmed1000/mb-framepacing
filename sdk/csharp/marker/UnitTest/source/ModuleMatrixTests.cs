//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* ModuleMatrix: its sizes, comparing two matrices module by module (the padding bits do not count), and what an empty matrix draws.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class ModuleMatrixTests
  {
    private static byte[] SyncBits()
    {
      ModuleMatrix matrix = TestMarkers.Encode(new Payload(MarkerKind.Sync, 3, 1, MarkerFlags.NoFlags, NanosecondTimeSpan.Zero));
      return matrix.Bits.ToArray();
    }

    [Test]
    public void TheSizesFollowTheKind()
    {
      Assert.That((ModuleMatrix.MainSize, ModuleMatrix.SyncSize), Is.EqualTo((41, 25)));
      Assert.That(ModuleMatrix.SizeFor(MarkerKind.Frame), Is.EqualTo(41));
      Assert.That(ModuleMatrix.SizeFor(MarkerKind.SequenceStart), Is.EqualTo(41));
      Assert.That(ModuleMatrix.SizeFor(MarkerKind.SequenceEnd), Is.EqualTo(41));
      Assert.That(ModuleMatrix.SizeFor(MarkerKind.Sync), Is.EqualTo(25));
      Assert.That(ModuleMatrix.PackedModuleByteCount(ModuleMatrix.MainSize), Is.EqualTo(211));
      Assert.That(ModuleMatrix.PackedModuleByteCount(ModuleMatrix.SyncSize), Is.EqualTo(79));
      Assert.That(ModuleMatrix.MaxPackedModuleByteCount, Is.EqualTo(211));
      Assert.That(ModuleMatrix.PackedModuleByteCount(0), Is.Zero);
      Assert.That(ModuleMatrix.PackedModuleByteCount(-1), Is.Zero);
    }

    [Test]
    public void SequenceEqual_ComparesTheModulesAndNotThePadding()
    {
      byte[] bits = SyncBits();
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, bits, out var matrix), Is.True);

      byte[] same = (byte[])bits.Clone();
      same[same.Length - 1] ^= 0x7F; // only padding
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, same, out var samePadded), Is.True);
      Assert.That(matrix.SequenceEqual(samePadded), Is.True);

      byte[] lastModule = (byte[])bits.Clone();
      lastModule[lastModule.Length - 1] ^= 0x80; // the last module
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, lastModule, out var otherLast), Is.True);
      Assert.That(matrix.SequenceEqual(otherLast), Is.False);

      byte[] firstModule = (byte[])bits.Clone();
      firstModule[0] ^= 0x80;
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, firstModule, out var otherFirst), Is.True);
      Assert.That(matrix.SequenceEqual(otherFirst), Is.False);

      // Another size is another matrix; two empty ones are equal
      Assert.That(matrix.SequenceEqual(default), Is.False);
      Assert.That(default(ModuleMatrix).SequenceEqual(default), Is.True);
    }

    [Test]
    public void TryFromBits_TakesTheMarkerSizesOnly()
    {
      var bits = new byte[ModuleMatrix.PackedModuleByteCount(ModuleMatrix.MainSize)];
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, bits, out var sync), Is.True);
      Assert.That((sync.Size, sync.Bits.Length), Is.EqualTo((25, 79)));
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.MainSize, bits, out var main), Is.True);
      Assert.That((main.Size, main.Bits.Length), Is.EqualTo((41, 211)));
      // The other QR sizes are not markers: nothing draws their grid
      foreach (int size in new[] { 21, 29, 33, 37 })
      {
        Assert.That(ModuleMatrix.TryFromBits(size, bits, out var other), Is.False, size.ToString());
        Assert.That(other.IsEmpty, Is.True);
      }
      foreach (int size in new[] { -25, 0, 17, 24, 26, 45 })
        Assert.That(ModuleMatrix.TryFromBits(size, bits, out _), Is.False, size.ToString());
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.MainSize, bits.AsSpan(0, 210), out _), Is.False, "a byte too few");
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, bits.AsSpan(0, 78), out _), Is.False, "a byte too few");
    }

    [Test]
    public void AnEmptyMatrixDrawsNothing()
    {
      ModuleMatrix empty = default;
      Assert.That(empty.IsEmpty, Is.True);
      var vertices = new Vertex[8];
      var indices = new int[12];
      Assert.That(FrameMarker.ModulesToQuads(empty, Options.Default, default, new MarkerQuad[4]), Is.Zero);
      Assert.That(FrameMarker.ModulesToTriangles(empty, Options.Default, default, vertices), Is.Zero);
      IndexedCount indexed = FrameMarker.ModulesToIndexed(empty, Options.Default, default, vertices, indices);
      Assert.That((indexed.VertexCount, indexed.IndexCount), Is.EqualTo((0, 0)));
      Assert.That(FrameMarker.ModulesToGridIndices(empty, indices), Is.Zero);
      Assert.That(FrameMarker.ModulesToBitmap(empty, new Options(1, 0), default, new byte[16], 4, 4, PixelFormat.R8), Is.False);
    }

    [Test]
    public void ABitmapWithoutRowsHasNothingToDraw()
    {
      byte[] bits = SyncBits();
      Assert.That(ModuleMatrix.TryFromBits(ModuleMatrix.SyncSize, bits, out var matrix), Is.True);
      var pixels = new byte[64];
      Array.Fill(pixels, (byte)128);
      Assert.That(FrameMarker.ModulesToBitmap(matrix, new Options(1, 4), default, pixels, 64, 0, PixelFormat.R8), Is.True);
      Assert.That(pixels, Is.All.EqualTo(128));
      Assert.That(FrameMarker.ModulesToBitmap(matrix, new Options(1, 4), default, pixels, 64, -1, PixelFormat.R8), Is.False);
    }
  }
}
