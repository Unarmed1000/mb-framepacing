//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ test (marker/cpp/tests/FrameMarkerTests.cpp) so both sides agree byte for byte.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerPayloadTests
  {
    [Test]
    public void Encode_MatchesDocumentedLayout()
    {
      var payload = new MarkerPayload(
        0x0102030405060708UL,
        0x1112131415161718L,
        0x21222324u,
        MarkerKind.SequenceEnd,
        0x3132333435363738L,
        0x41424344u,
        0x5152535455565758L,
        0x61626364u,
        0x71727374u,
        MB.FrameMarker.MarkerFlags.Static
      );
      byte[] expected =
      [
        (byte)'M',
        (byte)'F',
        1,
        2,
        // run id
        0x24,
        0x23,
        0x22,
        0x21,
        // frame index
        0x08,
        0x07,
        0x06,
        0x05,
        0x04,
        0x03,
        0x02,
        0x01,
        // flags
        0x01,
        // animation time
        0x18,
        0x17,
        0x16,
        0x15,
        0x14,
        0x13,
        0x12,
        0x11,
        // preferred frame time
        0x74,
        0x73,
        0x72,
        0x71,
        // target frame time
        0x44,
        0x43,
        0x42,
        0x41,
        // intended display time
        0x38,
        0x37,
        0x36,
        0x35,
        0x34,
        0x33,
        0x32,
        0x31,
        // CPU start time
        0x58,
        0x57,
        0x56,
        0x55,
        0x54,
        0x53,
        0x52,
        0x51,
        // CPU busy
        0x64,
        0x63,
        0x62,
        0x61,
      ];
      Assert.That(payload.Encode(), Is.EqualTo(expected));
    }

    [TestCase(0UL, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1UL, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7UL, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42UL, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrip(ulong frameIndex, long ticks, uint runId, MarkerKind kind)
    {
      var payload = new MarkerPayload(frameIndex, ticks, runId, kind);
      Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, kind == MarkerKind.SequenceStart ? Is.EqualTo(StartMetadata.Empty) : Is.Null);
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var sequenceId = MB.FrameMarker.SequenceId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
      var metadata = StartMetadata.Create(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc), sequenceId);
      var payload = new MarkerPayload(10, 20, 30, MarkerKind.SequenceStart, 40, 50, 60);
      var bytes = payload.Encode(metadata);
      Assert.That(bytes, Has.Length.EqualTo(MarkerPayload.StartByteCount));
      Assert.That(MarkerPayload.TryDecode(bytes, out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, Is.EqualTo(metadata));
      Assert.That(start!.StartTimeUtc, Is.EqualTo(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc)));
      Assert.That(start.SequenceText, Is.EqualTo("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Test]
    public void StartMetadata_Tag()
    {
      Assert.That(StartMetadata.FromTag(0, "menu benchmark").SequenceText, Is.EqualTo("menu benchmark"));
      Assert.That(StartMetadata.Empty.SequenceText, Is.Null);
      Assert.Throws<ArgumentException>(() => StartMetadata.FromTag(0, "seventeen chars!!"));
      Assert.Throws<ArgumentException>(() => StartMetadata.FromTag(0, "æøå"));
    }

    [Test]
    public void FrameMarker_IgnoresMetadata()
    {
      var bytes = new MarkerPayload(1, 2, 3, MarkerKind.Frame).Encode(StartMetadata.FromTag(5, "ignored"));
      Assert.That(bytes, Has.Length.EqualTo(MarkerPayload.ByteCount));
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new MarkerPayload(1, 2, 3).Encode();
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, MarkerPayload.ByteCount - 1), out _), Is.False);

      bytes[0] = (byte)'X';
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
      bytes[0] = (byte)'M';

      bytes[2] = 2;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
      bytes[2] = 1;

      bytes[3] = 3;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);

      // A start marker must carry its metadata block
      bytes[3] = (byte)MarkerKind.SequenceStart;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsWrongStartLength()
    {
      var bytes = new MarkerPayload(1, 2, 3, MarkerKind.SequenceStart).Encode(StartMetadata.FromTag(0, "ab"));
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.True);
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _), Is.False);
      Assert.That(MarkerPayload.TryDecode([.. bytes, 0], out _), Is.False);
    }
  }
}
