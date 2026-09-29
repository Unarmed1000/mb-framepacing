//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ tests (marker/cpp/tests/FrameMarkerTests.cpp).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Text;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  [TestFixture]
  public class PayloadTests
  {
    [Test]
    public void Encode_ProducesTheDocumentedLittleEndianLayout()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      int count = Marker.EncodePayload(
        new Payload(
          0x0102030405060708u,
          0x1112131415161718,
          0x21222324u,
          MarkerKind.SequenceEnd,
          0x3132333435363738,
          0x41424344u,
          0x5152535455565758,
          0x61626364u,
          0x71727374u,
          MarkerFlags.Static
        ),
        default,
        bytes
      );
      Assert.That(count, Is.EqualTo(Marker.PayloadByteCount));
      var expected = new byte[]
      {
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
      };
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void NegativeTicks_AreStoredAsTwosComplement()
    {
      var bytes = new byte[Marker.PayloadByteCount];
      Marker.EncodePayload(new Payload(0, -1), default, bytes);
      Assert.That(bytes.AsSpan(17, 8).ToArray(), Is.All.EqualTo((byte)0xFF));
    }

    [TestCase(0ul, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1ul, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7ul, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42ul, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrips(ulong frame, long ticks, uint run, MarkerKind kind)
    {
      var payload = new Payload(frame, ticks, run, kind);
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      int count = Marker.EncodePayload(payload, default, bytes);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    [TestCase(0L, 0u)]
    [TestCase(1_234_567_890_123L, 166_667u)]
    [TestCase(long.MinValue, uint.MaxValue)]
    [TestCase(long.MaxValue, 333_333u)]
    public void PacingFields_RoundTrip(long intendedDisplayTicks, uint targetFrameTicks)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(7, 8, 9, kind, intendedDisplayTicks, targetFrameTicks, -intendedDisplayTicks / 2, targetFrameTicks / 3);
        var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
        int count = Marker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That(decoded.IntendedDisplayTicks, Is.EqualTo(intendedDisplayTicks));
        Assert.That(decoded.TargetFrameTicks, Is.EqualTo(targetFrameTicks));
        Assert.That(decoded.CpuStartTicks, Is.EqualTo(-intendedDisplayTicks / 2));
        Assert.That(decoded.CpuBusyTicks, Is.EqualTo(targetFrameTicks / 3));
      }
    }

    [TestCase(166_667u, MarkerFlags.None)]
    [TestCase(Marker.OnDemandFrameTicks, MarkerFlags.Static)]
    [TestCase(10_000_000u, MarkerFlags.Static)]
    [TestCase(0u, (MarkerFlags)0x81)]
    public void PreferredFrameTimeAndFlags_RoundTrip(uint preferredFrameTicks, MarkerFlags flags)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(7, 8, 9, kind, 10, 333_333, 11, 12, preferredFrameTicks, flags);
        var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
        int count = Marker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(bytes[16], Is.EqualTo((byte)flags), "the flags byte, reserved bits included");
        Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That((decoded.PreferredFrameTicks, decoded.Flags), Is.EqualTo((preferredFrameTicks, flags)));
      }
    }

    [Test]
    public void OldHeaderLengths_AreRejected()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount + 1];
      int count = Marker.EncodePayload(new Payload(1, 2, 3), default, bytes);
      Assert.That(count, Is.EqualTo(53));
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, 52), out _, out _), Is.False);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, 54), out _, out _), Is.False);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, 48), out _, out _), Is.False, "the older 48 byte header");
    }

    [Test]
    public void SyncMarker_CarriesOnlyTheRunIdAndTheFrameIndex()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      int count = Marker.EncodePayload(new Payload(0x0102030405060708u, 123, 4, MarkerKind.Sync, 5, 6), default, bytes);
      Assert.That(count, Is.EqualTo(Marker.SyncPayloadByteCount));
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(new byte[] { (byte)'M', (byte)'F', 1, 3, 4, 0, 0, 0, 8, 7, 6, 5, 4, 3, 2, 1 }));
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(new Payload(0x0102030405060708u, 0, 4, MarkerKind.Sync)));
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, count + 1), out _, out _), Is.False);
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount + 6];
      var payload = new Payload(10, 20, 30, MarkerKind.SequenceStart, 40, 50, 60, 70);
      var id = new SequenceId(0x0011_2233_4455_6677, 0x8899_AABB_CCDD_EEFF);
      int count = Marker.EncodePayload(payload, new StartMetadata(638_000_000_000_000_000, id), bytes.AsSpan(5));
      Assert.That(count, Is.EqualTo(Marker.StartPayloadByteCount));
      Assert.That(count, Is.EqualTo(77));
      // The sequence id's 16 bytes as they are, at offset 61
      Assert.That(
        bytes.AsSpan(5 + 61, 16).ToArray(),
        Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF })
      );

      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(5, count), out var decoded, out var metadata), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(metadata.UtcTicks, Is.EqualTo(638_000_000_000_000_000));
      Assert.That(metadata.SequenceId, Is.EqualTo(id));

      // A start marker of another length is rejected; frame payloads ignore the metadata
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(5, count - 1), out _, out _), Is.False);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(5, count + 1), out _, out _), Is.False);
      Assert.That(Marker.EncodePayload(new Payload(1, 2, 3), new StartMetadata(5, id), bytes), Is.EqualTo(Marker.PayloadByteCount));
    }

    [Test]
    public void Encode_RejectsSmallBuffers()
    {
      var start = new Payload(1, 2, 3, MarkerKind.SequenceStart);
      Assert.That(Marker.EncodePayload(start, default, new byte[Marker.StartPayloadByteCount - 1]), Is.Zero);
      Assert.That(Marker.EncodePayload(new Payload(1, 2), default, new byte[Marker.PayloadByteCount - 1]), Is.Zero);
      Assert.That(Marker.EncodePayload(start, default, new byte[Marker.StartPayloadByteCount]), Is.EqualTo(Marker.StartPayloadByteCount));
    }

    [Test]
    public void SequenceId_TextGuidAndDisplay()
    {
      Assert.That(SequenceId.TryFromText("bench-2026-09-28", out var text), Is.True, "16 characters fit");
      Assert.That(text.ToString(), Is.EqualTo("bench-2026-09-28"));
      Assert.That(SequenceId.TryFromText("run 7", out var shortText), Is.True);
      Assert.That(shortText.ToString(), Is.EqualTo("run 7"), "padded with zero bytes, shown without them");
      Assert.That(SequenceId.TryFromText("seventeen chars!!", out _), Is.False, "too long");
      Assert.That(SequenceId.TryFromText("tab\there", out _), Is.False, "not printable");
      Assert.That(SequenceId.TryFromText("æøå", out _), Is.False, "not ASCII");

      var guid = Guid.Parse("3f2a1b4c-5d6e-7f80-9102-a3b4c5d6e7f8");
      var fromGuid = SequenceId.FromGuid(guid);
      Assert.That(fromGuid.ToString(), Is.EqualTo("3f2a1b4c-5d6e-7f80-9102-a3b4c5d6e7f8"), "the bytes in the order the UUID text shows them");
      var bytes = new byte[SequenceId.ByteCount];
      Assert.That(fromGuid.TryCopyTo(bytes), Is.True);
      Assert.That(bytes[0], Is.EqualTo(0x3f));
      Assert.That(SequenceId.FromBytes(bytes), Is.EqualTo(fromGuid));
      Assert.That(new SequenceId(0, 1).ToString(), Is.EqualTo("00000000-0000-0000-0000-000000000001"), "not text: hex");
      Assert.That(default(SequenceId).IsEmpty, Is.True);
    }

    [Test]
    public void SequenceId_FromGuid_MatchesTheUuidText()
    {
      var random = new Random(1234);
      var raw = new byte[16];
      for (int i = 0; i < 200; ++i)
      {
        random.NextBytes(raw);
        var guid = new Guid(raw);
        Assert.That(SequenceId.FromGuid(guid).ToString(), Is.EqualTo(guid.ToString("D")), guid.ToString());
      }
      Assert.That(SequenceId.FromGuid(Guid.Empty).IsEmpty, Is.True);
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new byte[Marker.PayloadByteCount];
      Marker.EncodePayload(new Payload(1, 2), default, bytes);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, Marker.PayloadByteCount - 1), out _, out _), Is.False, "short");
      bytes[0] = (byte)'X';
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "magic");
      bytes[0] = (byte)'M';
      bytes[2] = 2;
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "format version");
      bytes[2] = 1;
      bytes[3] = 3;
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "kind");
      bytes[3] = 0;
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.True);

      // A start marker without its metadata block, or with a byte too many
      Marker.EncodePayload(new Payload(1, 2, 3, MarkerKind.SequenceStart), default, bytes = new byte[Marker.StartPayloadByteCount + 1]);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, Marker.PayloadByteCount), out _, out _), Is.False);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False);
      Assert.That(Marker.TryDecodePayload(bytes.AsSpan(0, Marker.StartPayloadByteCount), out _, out _), Is.True);
    }

    [Test]
    public void Ticks_MatchDateTimeAndTimeSpan()
    {
      var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      Assert.That(Marker.ToDateTimeTicks(time), Is.EqualTo(639_028_224_000_000_000));
      Assert.That(StartMetadata.Create(time, new SequenceId(1, 2)).UtcTicks, Is.EqualTo(time.Ticks));
      Assert.That(Marker.SecondsToTicks(1.5), Is.EqualTo(TimeSpan.FromSeconds(1.5).Ticks));
      Assert.That(Marker.SecondsToTicks(1.0 / 60), Is.EqualTo(166_667));
    }
  }
}
