//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests, and the tools' side of the marker: MarkerPayload holds its times in nanoseconds as the marker does, each as it is
//* both ways. The expected bytes are the C++ test's (sdk/cpp/marker/tests/FrameMarkerTests.cpp).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
{
  [TestFixture]
  public class MarkerPayloadTests
  {
    // The longest frame time and the longest CPU busy a marker holds, in nanoseconds
    private const uint MaxFrameNanoseconds = 4_294_967_294;
    private const uint MaxCpuBusyNanoseconds = 4_294_967_295;

    [Test]
    public void Encode_MatchesDocumentedLayout()
    {
      // The C++ test's values, in nanoseconds
      var payload = new MarkerPayload(
        MarkerKind.SequenceEnd,
        0x21222324u,
        0x0102030405060708UL,
        MB.FramePacing.Marker.MarkerFlags.StaticAfter,
        new NanosecondTimeSpan(0x1112131415161718),
        PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(0x71727374),
        TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(0x41424344),
        IntendedDisplayTime: new NanosecondTickCount(0x3132333435363738),
        CpuStartTime: new NanosecondTickCount(0x5152535455565758),
        CpuBusy: NanosecondTimeDuration.FromNanoseconds(0x61626364)
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
        // CRC (0x7ED16A3D: what Python's binascii.crc32 gives for the 53 bytes before it)
        0x3D,
        0x6A,
        0xD1,
        0x7E,
      ];
      Assert.That(payload.Encode(), Is.EqualTo(expected));
      Assert.That(MarkerPayload.TryDecode(expected, out var decoded), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    // Any time, to the first and the last nanosecond a marker holds (about 292 years either side of zero)
    [TestCase(0UL, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1UL, 16_666_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7UL, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42UL, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrip(ulong frameIndex, long nanoseconds, uint runId, MarkerKind kind)
    {
      var payload = new MarkerPayload(kind, runId, frameIndex, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(nanoseconds));
      Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, kind == MarkerKind.SequenceStart ? Is.EqualTo(StartMetadata.Empty) : Is.Null);
    }

    [TestCase(0L, 0u, 0u, 0u)]
    [TestCase(123_456_789_012_345L, 16_666_667u, 33_333_333u, 8_000_001u)]
    [TestCase(long.MaxValue, MaxFrameNanoseconds, 1u, MaxCpuBusyNanoseconds)]
    [TestCase(long.MinValue, uint.MaxValue, uint.MaxValue, 1u)] // on demand
    public void EveryTime_RoundTrips(long clockNanoseconds, uint preferredFrameNanoseconds, uint targetFrameNanoseconds, uint cpuBusyNanoseconds)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new MarkerPayload(
          kind,
          9,
          7,
          MB.FramePacing.Marker.MarkerFlags.StaticBefore,
          new NanosecondTimeSpan(clockNanoseconds / -3),
          PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(preferredFrameNanoseconds),
          TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(targetFrameNanoseconds),
          IntendedDisplayTime: new NanosecondTickCount(clockNanoseconds),
          CpuStartTime: new NanosecondTickCount(clockNanoseconds / -2),
          CpuBusy: NanosecondTimeDuration.FromNanoseconds(cpuBusyNanoseconds)
        );
        Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That(MarkerPayload.FromFrameMarker(payload.ToFrameMarker()), Is.EqualTo(payload));
      }
    }

    [Test]
    public void ToAMarker_EveryTimeIsAsItIs()
    {
      var marker = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.NoFlags,
        new NanosecondTimeSpan(-3),
        PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(16_666_667),
        TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(33_333_333),
        IntendedDisplayTime: new NanosecondTickCount(-5),
        CpuStartTime: new NanosecondTickCount(6),
        CpuBusy: NanosecondTimeDuration.FromNanoseconds(7)
      ).ToFrameMarker();
      Assert.That(marker.AnimationTime, Is.EqualTo(new NanosecondTimeSpan(-3)));
      Assert.That(marker.PreferredFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16_666_667)));
      Assert.That(marker.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(33_333_333)));
      Assert.That(marker.IntendedDisplayTime, Is.EqualTo(new NanosecondTickCount(-5)));
      Assert.That(marker.CpuStartTime, Is.EqualTo(new NanosecondTickCount(6)));
      Assert.That(marker.CpuBusy, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(7)));

      // 0 is unknown on both sides
      var unknown = new MarkerPayload(MarkerKind.Frame, 1, 2, MB.FramePacing.Marker.MarkerFlags.NoFlags, NanosecondTimeSpan.Zero).ToFrameMarker();
      Assert.That(unknown, Is.EqualTo(new MB.FramePacing.Marker.Payload(MB.FramePacing.Marker.MarkerKind.Frame, 1, 2, default, default)));
      Assert.That(MarkerPayload.FromFrameMarker(default), Is.EqualTo(default(MarkerPayload)));
    }

    [Test]
    public void FromAMarker_EveryTimeIsAsItIs()
    {
      // A sixtieth of a second is 16 666 667 ns in the marker and here: an interval, a point and a duration alike, below zero too
      var payload = MarkerPayload.FromFrameMarker(
        new MB.FramePacing.Marker.Payload(
          MB.FramePacing.Marker.MarkerKind.Frame,
          1,
          2,
          MB.FramePacing.Marker.MarkerFlags.NoFlags,
          new NanosecondTimeSpan(-16_666_667),
          preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(16_666_667),
          targetFrameTime: NanosecondTimeDuration.FromNanoseconds(49),
          intendedDisplayTime: new NanosecondTickCount(-16_666_667),
          cpuStartTime: new NanosecondTickCount(33_333_333),
          cpuBusy: NanosecondTimeDuration.FromNanoseconds(8_000_099)
        )
      );
      Assert.That(payload.AnimationTime, Is.EqualTo(new NanosecondTimeSpan(-16_666_667)));
      Assert.That(payload.PreferredFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16_666_667)));
      Assert.That(payload.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(49)));
      Assert.That(payload.IntendedDisplayTime, Is.EqualTo(new NanosecondTickCount(-16_666_667)));
      Assert.That(payload.CpuStartTime, Is.EqualTo(new NanosecondTickCount(33_333_333)));
      Assert.That(payload.CpuBusy, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(8_000_099)));
    }

    // Nanoseconds that are on no whole tick of 100 ns, either side of zero, and the first and the last a marker holds
    [TestCase(1)]
    [TestCase(49)]
    [TestCase(50)]
    [TestCase(51)]
    [TestCase(150)]
    [TestCase(-1)]
    [TestCase(-50)]
    [TestCase(-149)]
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void FromAMarker_EveryNanosecondIsKept(long nanoseconds)
    {
      var payload = MarkerPayload.FromFrameMarker(
        new MB.FramePacing.Marker.Payload(
          MB.FramePacing.Marker.MarkerKind.Frame,
          1,
          2,
          MB.FramePacing.Marker.MarkerFlags.NoFlags,
          new NanosecondTimeSpan(nanoseconds),
          intendedDisplayTime: new NanosecondTickCount(nanoseconds),
          cpuStartTime: new NanosecondTickCount(nanoseconds)
        )
      );
      Assert.That(payload.AnimationTime, Is.EqualTo(new NanosecondTimeSpan(nanoseconds)));
      Assert.That(payload.IntendedDisplayTime, Is.EqualTo(new NanosecondTickCount(nanoseconds)));
      Assert.That(payload.CpuStartTime, Is.EqualTo(new NanosecondTickCount(nanoseconds)));
    }

    [Test]
    public void OnDemand_IsTheMarkersValue()
    {
      Assert.That(MarkerPayload.OnDemandFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.OnDemandFrameTime));
      Assert.That(MarkerPayload.OnDemandFrameTime.Nanoseconds, Is.EqualTo(4_294_967_295));
      var onDemand = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.StaticAfter,
        new NanosecondTimeSpan(300),
        PreferredFrameTime: MarkerPayload.OnDemandFrameTime,
        TargetFrameTime: MarkerPayload.OnDemandFrameTime
      );
      var marker = onDemand.ToFrameMarker();
      Assert.That(marker.PreferredFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.OnDemandFrameTime));
      Assert.That(marker.TargetFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.OnDemandFrameTime));
      // The marker's four bytes: 0xFFFFFFFF, at offsets 25 and 29
      var bytes = onDemand.Encode();
      Assert.That(bytes.AsSpan(25, 8).ToArray(), Is.All.EqualTo((byte)0xFF));
      Assert.That(MarkerPayload.TryDecode(bytes, out var decoded), Is.True);
      Assert.That(decoded, Is.EqualTo(onDemand));
      Assert.That(decoded.PreferredFrameTime, Is.EqualTo(MarkerPayload.OnDemandFrameTime));
      Assert.That(decoded.TargetFrameTime, Is.EqualTo(MarkerPayload.OnDemandFrameTime));

      // CPU busy has no on demand: the same count of nanoseconds is a duration as any other, the longest a marker carries
      var busy = onDemand with
      {
        CpuBusy = MarkerPayload.OnDemandFrameTime,
      };
      Assert.That(busy.ToFrameMarker().CpuBusy, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxCpuBusy));
      Assert.That(MarkerPayload.TryDecode(busy.Encode(), out decoded), Is.True);
      Assert.That(decoded, Is.EqualTo(busy), "the longest a marker carries, as it is");
    }

    [Test]
    public void ADurationLongerThanAMarkerCarries_IsHeldAsTheLongestItCarries()
    {
      // A duration reaches 292 years, a marker's four bytes of nanoseconds 4.29 s: the marker library's payload caps the rest
      var tooLong = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.NoFlags,
        new NanosecondTimeSpan(300),
        PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(5 * NanosecondTimeSpan.NanosecondsPerSecond),
        TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(MarkerPayload.OnDemandFrameTime.Nanoseconds + 1),
        CpuBusy: NanosecondTimeDuration.FromNanoseconds(5 * NanosecondTimeSpan.NanosecondsPerSecond)
      );
      var marker = tooLong.ToFrameMarker();
      Assert.That(marker.PreferredFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxFrameTime));
      Assert.That(marker.TargetFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxFrameTime));
      Assert.That(marker.CpuBusy, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxCpuBusy));
      // What comes back is what the marker carried
      Assert.That(MarkerPayload.TryDecode(tooLong.Encode(), out var decoded), Is.True);
      Assert.That(
        decoded,
        Is.EqualTo(
          tooLong with
          {
            PreferredFrameTime = MB.FramePacing.Marker.Payload.MaxFrameTime,
            TargetFrameTime = MB.FramePacing.Marker.Payload.MaxFrameTime,
            CpuBusy = MB.FramePacing.Marker.Payload.MaxCpuBusy,
          }
        ),
        "never on demand: only on demand reads as on demand"
      );
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var sequenceId = MB.FramePacing.Marker.SequenceId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
      var metadata = StartMetadata.Create(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc), sequenceId);
      var payload = new MarkerPayload(
        MarkerKind.SequenceStart,
        30,
        10,
        MB.FramePacing.Marker.MarkerFlags.NoFlags,
        new NanosecondTimeSpan(2_000),
        TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(5_000),
        IntendedDisplayTime: new NanosecondTickCount(4_000),
        CpuStartTime: new NanosecondTickCount(6_000)
      );
      var bytes = payload.Encode(metadata);
      Assert.That(bytes, Has.Length.EqualTo(81), "a start marker's payload");
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
      var bytes = new MarkerPayload(MarkerKind.Frame, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(200)).Encode(
        StartMetadata.FromTag(5, "ignored")
      );
      Assert.That(bytes, Has.Length.EqualTo(57), "a frame marker's payload");
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new MarkerPayload(MarkerKind.Frame, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(200)).Encode();
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.True);
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, 56), out _), Is.False);
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, 53), out _), Is.False, "the header without its CRC");

      // One bit of a field: only the CRC tells
      bytes[8] ^= 1;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
      bytes[8] ^= 1;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.True);

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
      var bytes = new MarkerPayload(MarkerKind.SequenceStart, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(200)).Encode(
        StartMetadata.FromTag(0, "ab")
      );
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.True);
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _), Is.False);
      Assert.That(MarkerPayload.TryDecode([.. bytes, 0], out _), Is.False);
    }
  }
}
