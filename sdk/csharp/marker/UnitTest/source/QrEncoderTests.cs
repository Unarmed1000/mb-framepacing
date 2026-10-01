//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The QR encoder's limits: versions 1 to 6, the smallest version of the range that fits the data, and data that does not fit. What it
//* encodes is checked symbol by symbol against the golden module digest (CrossLanguageTests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class QrEncoderTests
  {
    [Test]
    public void AVersionRangeOutsideOneToSixIsRefused()
    {
      var encoder = new QrEncoder();
      var data = new byte[4];
      Assert.That(encoder.Encode(data, QrEncoder.MinVersion - 1, 6), Is.False);
      Assert.That(encoder.Encode(data, 1, QrEncoder.MaxVersion + 1), Is.False);
      Assert.That(encoder.Encode(data, 3, 2), Is.False);
      Assert.That(encoder.Encode(data, QrEncoder.MinVersion, QrEncoder.MaxVersion), Is.True);
    }

    [Test]
    public void TheSmallestVersionOfTheRangeThatFitsIsUsed()
    {
      var encoder = new QrEncoder();
      // Byte mode, level M: version 1 holds 14 bytes, version 2 26, version 6 106
      Assert.That(encoder.Encode(new byte[14], 1, 6), Is.True);
      Assert.That(encoder.Size, Is.EqualTo(21));
      Assert.That(encoder.Encode(new byte[15], 1, 6), Is.True);
      Assert.That(encoder.Size, Is.EqualTo(25));
      Assert.That(encoder.Encode(new byte[WireFormat.SyncQrCapacityBytes], 2, 2), Is.True);
      Assert.That(encoder.Encode(new byte[WireFormat.QrCapacityBytes], 1, 6), Is.True);
      Assert.That(encoder.Size, Is.EqualTo(ModuleMatrix.MainSize));
    }

    [Test]
    public void DataThatDoesNotFitTheRangeIsRefused()
    {
      var encoder = new QrEncoder();
      Assert.That(encoder.Encode(new byte[WireFormat.SyncQrCapacityBytes + 1], 2, 2), Is.False);
      Assert.That(encoder.Encode(new byte[WireFormat.QrCapacityBytes + 1], 1, 6), Is.False);
    }
  }
}
