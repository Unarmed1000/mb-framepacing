//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* DeviceTimestamp's three states, and the record header's bytes for each.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using MB.FramePacing.Data;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class DeviceTimestampTests
  {
    [Test]
    public void Default_IsUnknown()
    {
      var value = default(DeviceTimestamp);

      Assert.That(value, Is.EqualTo(DeviceTimestamp.Unknown));
      Assert.That(value.IsKnown, Is.False);
      Assert.That(value.IsPending, Is.False);
      Assert.That(value.ToNullable(), Is.Null);
      Assert.That(() => value.Time, Throws.InvalidOperationException);
      Assert.That(value.ToString(), Is.EqualTo("unknown"));
    }

    [Test]
    public void Pending_IsNeitherKnownNorUnknown()
    {
      var value = DeviceTimestamp.Pending;

      Assert.That(value.IsPending, Is.True);
      Assert.That(value.IsKnown, Is.False);
      Assert.That(value, Is.Not.EqualTo(DeviceTimestamp.Unknown));
      Assert.That(value != DeviceTimestamp.Unknown, Is.True);
      Assert.That(value.ToNullable(), Is.Null);
      Assert.That(() => value.Time, Throws.InvalidOperationException);
      Assert.That(value.ToString(), Is.EqualTo("pending"));
    }

    [Test]
    public void Known_HasItsTime()
    {
      var time = TickCount64.FromMilliseconds(1500);
      var value = new DeviceTimestamp(time);

      Assert.That(value.IsKnown, Is.True);
      Assert.That(value.IsPending, Is.False);
      Assert.That(value.Time, Is.EqualTo(time));
      Assert.That(value.ToNullable(), Is.EqualTo(time));
      Assert.That(value == new DeviceTimestamp(time), Is.True);
      Assert.That(value.GetHashCode(), Is.EqualTo(new DeviceTimestamp(time).GetHashCode()));
      Assert.That(value, Is.Not.EqualTo(new DeviceTimestamp(TickCount64.FromMilliseconds(1501))));
      Assert.That(value.ToString(), Is.EqualTo(time.ToString()));
    }

    [Test]
    public void Known_AtTimeZero_IsNotUnknown()
    {
      var value = new DeviceTimestamp(default);

      Assert.That(value.IsKnown, Is.True);
      Assert.That(value, Is.Not.EqualTo(DeviceTimestamp.Unknown));
      Assert.That(value.Equals((object)DeviceTimestamp.Unknown), Is.False);
      Assert.That(value.Equals("0"), Is.False);
    }

    /// <summary>The file's bytes: a time as its ticks, unknown as the capture data format's long.MinValue, pending (ring only) the next value.</summary>
    [TestCase(0, long.MinValue)]
    [TestCase(1, long.MinValue + 1)]
    [TestCase(2, 123_456L)]
    public void RecordHeader_WritesAndReadsEveryState(int state, long expectedTicks)
    {
      var deviceTime = state switch
      {
        0 => DeviceTimestamp.Unknown,
        1 => DeviceTimestamp.Pending,
        _ => new DeviceTimestamp(new TickCount64(expectedTicks)),
      };
      var header = new CaptureRecordHeader(7, new TickCount64(1000), deviceTime, 2, 32);
      Span<byte> bytes = stackalloc byte[CaptureFileHeader.RecordHeaderSize];

      header.Write(bytes);

      Assert.That(BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(16)), Is.EqualTo(expectedTicks));
      Assert.That(CaptureRecordHeader.Read(bytes), Is.EqualTo(header));
    }
  }
}
