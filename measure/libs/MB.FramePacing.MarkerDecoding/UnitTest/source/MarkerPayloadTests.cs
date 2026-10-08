//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests, and the tools' side of the marker: MarkerPayload holds its times in ticks of 100 ns, the marker in nanoseconds, so
//* the expected bytes are the C++ test's (sdk/cpp/marker/tests/FrameMarkerTests.cpp) for times that are whole ticks.
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
    // The first and the last tick whose nanoseconds a marker holds: about 292 years either side of zero
    private const long MinMarkerTicks = long.MinValue / NanosecondTimeSpan.NanosecondsPerTick;
    private const long MaxMarkerTicks = long.MaxValue / NanosecondTimeSpan.NanosecondsPerTick;

    // The longest frame time and CPU busy a marker holds, as the nearest tick: 4.2949673 s
    private const uint MaxMarkerDurationTicks = 42_949_673;

    [Test]
    public void Encode_MatchesDocumentedLayout()
    {
      // The tools' times are ticks, the marker's nanoseconds: each value is the C++ test's nanoseconds cut to the tick, so it encodes
      // to that test's bytes with the part of a tick gone (0x1112131415161718 ns is 0x1112131415161700 as whole ticks)
      var payload = new MarkerPayload(
        MarkerKind.SequenceEnd,
        0x21222324u,
        0x0102030405060708UL,
        MB.FramePacing.Marker.MarkerFlags.StaticAfter,
        new TimeSpan(12_300_666_251_996_096),
        PreferredFrameTime: new TimeSpan32(19_033_260),
        TargetFrameTime: new TimeSpan32(10_948_616),
        IntendedDisplayTime: new TickCount64(35_449_521_560_180_631),
        CpuStartTime: new TickCount64(58_598_376_868_365_166),
        CpuBusy: new TimeSpan32(16_338_379)
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
        // animation time (0x1112131415161700 ns)
        0x00,
        0x17,
        0x16,
        0x15,
        0x14,
        0x13,
        0x12,
        0x11,
        // preferred frame time (0x71727330 ns)
        0x30,
        0x73,
        0x72,
        0x71,
        // target frame time (0x41424320 ns)
        0x20,
        0x43,
        0x42,
        0x41,
        // intended display time (0x31323334353636FC ns)
        0xFC,
        0x36,
        0x36,
        0x35,
        0x34,
        0x33,
        0x32,
        0x31,
        // CPU start time (0x51525354555656F8 ns)
        0xF8,
        0x56,
        0x56,
        0x55,
        0x54,
        0x53,
        0x52,
        0x51,
        // CPU busy (0x6162634C ns)
        0x4C,
        0x63,
        0x62,
        0x61,
        // CRC (0x2E6CF7D9: what Python's binascii.crc32 gives for the 53 bytes before it)
        0xD9,
        0xF7,
        0x6C,
        0x2E,
      ];
      Assert.That(payload.Encode(), Is.EqualTo(expected));
      Assert.That(MarkerPayload.TryDecode(expected, out var decoded), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    // The longest times are the longest a marker's nanoseconds hold (a TimeSpan reaches a hundred times as far)
    [TestCase(0UL, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1UL, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, MaxMarkerTicks, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7UL, MinMarkerTicks, 1u, MarkerKind.SequenceStart)]
    [TestCase(42UL, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrip(ulong frameIndex, long ticks, uint runId, MarkerKind kind)
    {
      var payload = new MarkerPayload(kind, runId, frameIndex, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(ticks));
      Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, kind == MarkerKind.SequenceStart ? Is.EqualTo(StartMetadata.Empty) : Is.Null);
    }

    [TestCase(0L, 0u, 0u, 0u)]
    [TestCase(1_234_567_890_123L, 166_667u, 333_333u, 80_000u)]
    [TestCase(MaxMarkerTicks, MaxMarkerDurationTicks, 1u, MaxMarkerDurationTicks)]
    [TestCase(MinMarkerTicks, uint.MaxValue, uint.MaxValue, 1u)] // on demand
    public void EveryTime_RoundTripsInTicks(long clockTicks, uint preferredFrameTicks, uint targetFrameTicks, uint cpuBusyTicks)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new MarkerPayload(
          kind,
          9,
          7,
          MB.FramePacing.Marker.MarkerFlags.StaticBefore,
          new TimeSpan(-clockTicks / 3),
          PreferredFrameTime: new TimeSpan32(preferredFrameTicks),
          TargetFrameTime: new TimeSpan32(targetFrameTicks),
          IntendedDisplayTime: new TickCount64(clockTicks),
          CpuStartTime: new TickCount64(-clockTicks / 2),
          CpuBusy: new TimeSpan32(cpuBusyTicks)
        );
        Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That(MarkerPayload.FromFrameMarker(payload.ToFrameMarker()), Is.EqualTo(payload));
      }
    }

    [Test]
    public void TheMarkerHoldsNanoseconds_ExactlyTheTicks()
    {
      var marker = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.NoFlags,
        new TimeSpan(-3),
        PreferredFrameTime: new TimeSpan32(166_667),
        TargetFrameTime: new TimeSpan32(333_333),
        IntendedDisplayTime: new TickCount64(-5),
        CpuStartTime: new TickCount64(6),
        CpuBusy: new TimeSpan32(7)
      ).ToFrameMarker();
      Assert.That(marker.AnimationTime, Is.EqualTo(new NanosecondTimeSpan(-300)));
      Assert.That(marker.PreferredFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16_666_700)));
      Assert.That(marker.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(33_333_300)));
      Assert.That(marker.IntendedDisplayTime, Is.EqualTo(new NanosecondTickCount(-500)));
      Assert.That(marker.CpuStartTime, Is.EqualTo(new NanosecondTickCount(600)));
      Assert.That(marker.CpuBusy, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(700)));

      // 0 is unknown on both sides
      var unknown = new MarkerPayload(MarkerKind.Frame, 1, 2, MB.FramePacing.Marker.MarkerFlags.NoFlags, TimeSpan.Zero).ToFrameMarker();
      Assert.That(unknown, Is.EqualTo(new MB.FramePacing.Marker.Payload(MB.FramePacing.Marker.MarkerKind.Frame, 1, 2, default, default)));
      Assert.That(MarkerPayload.FromFrameMarker(default), Is.EqualTo(default(MarkerPayload)));
    }

    [Test]
    public void AMarkersNanoseconds_AreTheNearestTick()
    {
      // A sixtieth of a second is 16 666 667 ns and the tick the tools' own times are on, 166 667: an interval, a point and a duration
      // alike, below zero too
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
      Assert.That(payload.AnimationTime, Is.EqualTo(new TimeSpan(-166_667)));
      Assert.That(payload.PreferredFrameTime, Is.EqualTo(new TimeSpan32(166_667)));
      Assert.That(payload.TargetFrameTime, Is.EqualTo(TimeSpan32.Zero), "less than half a tick");
      Assert.That(payload.IntendedDisplayTime, Is.EqualTo(new TickCount64(-166_667)));
      Assert.That(payload.CpuStartTime, Is.EqualTo(new TickCount64(333_333)));
      Assert.That(payload.CpuBusy, Is.EqualTo(new TimeSpan32(80_001)));
    }

    [TestCase(50, 0, Description = "half a tick: to the even tick, down")]
    [TestCase(150, 2, Description = "half a tick: to the even tick, up")]
    [TestCase(250, 2)]
    [TestCase(51, 1)]
    [TestCase(149, 1)]
    [TestCase(-50, 0)]
    [TestCase(-150, -2)]
    [TestCase(-51, -1)]
    [TestCase(-149, -1)]
    [TestCase(-1, 0)]
    [TestCase(-100, -1)]
    public void ATieGoesToTheEvenTick(long nanoseconds, long ticks)
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
      Assert.That(payload.AnimationTime, Is.EqualTo(new TimeSpan(ticks)));
      Assert.That(payload.IntendedDisplayTime, Is.EqualTo(new TickCount64(ticks)));
      Assert.That(payload.CpuStartTime, Is.EqualTo(new TickCount64(ticks)));
    }

    [Test]
    public void OnDemand_IsEachSidesOwnValue()
    {
      Assert.That(MarkerPayload.OnDemandFrameTime, Is.EqualTo(TimeSpan32.MaxValue));
      var onDemand = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.StaticAfter,
        new TimeSpan(3),
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

      // CPU busy has no on demand: the tools' largest value is a duration as any other, and too long for a marker
      var busy = onDemand with
      {
        CpuBusy = TimeSpan32.MaxValue,
      };
      Assert.That(busy.ToFrameMarker().CpuBusy, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxCpuBusy));
    }

    [Test]
    public void ADurationLongerThanAMarkerCarries_IsHeldAsTheLongestItCarries()
    {
      // A TimeSpan32 reaches 429 s, a marker's four bytes of nanoseconds 4.29 s: the marker library's payload caps the rest
      var tooLong = new MarkerPayload(
        MarkerKind.Frame,
        1,
        2,
        MB.FramePacing.Marker.MarkerFlags.NoFlags,
        new TimeSpan(3),
        PreferredFrameTime: new TimeSpan32(MaxMarkerDurationTicks + 1),
        TargetFrameTime: new TimeSpan32(uint.MaxValue - 1),
        CpuBusy: new TimeSpan32(MaxMarkerDurationTicks + 1)
      );
      var marker = tooLong.ToFrameMarker();
      Assert.That(marker.PreferredFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxFrameTime));
      Assert.That(marker.TargetFrameTime, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxFrameTime));
      Assert.That(marker.CpuBusy, Is.EqualTo(MB.FramePacing.Marker.Payload.MaxCpuBusy));
      Assert.That(MarkerPayload.TryDecode(tooLong.Encode(), out var decoded), Is.True);
      Assert.That(
        decoded,
        Is.EqualTo(
          tooLong with
          {
            PreferredFrameTime = new TimeSpan32(MaxMarkerDurationTicks),
            TargetFrameTime = new TimeSpan32(MaxMarkerDurationTicks),
            CpuBusy = new TimeSpan32(MaxMarkerDurationTicks),
          }
        ),
        "never on demand: only on demand reads as on demand"
      );
    }

    [Test]
    public void ATimeThatNanosecondsCanNotHold_Throws()
    {
      var payload = new MarkerPayload(MarkerKind.Frame, 1, 2, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(3));
      Assert.That(() => (payload with { AnimationTime = new TimeSpan(MaxMarkerTicks + 1) }).Encode(), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => (payload with { AnimationTime = new TimeSpan(MinMarkerTicks - 1) }).Encode(), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => (payload with { AnimationTime = TimeSpan.MaxValue }).ToFrameMarker(), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => (payload with { IntendedDisplayTime = new TickCount64(MaxMarkerTicks + 1) }).Encode(), Throws.TypeOf<OverflowException>());
      Assert.That(() => (payload with { CpuStartTime = new TickCount64(MinMarkerTicks - 1) }).Encode(), Throws.TypeOf<OverflowException>());
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
        new TimeSpan(20),
        TargetFrameTime: new TimeSpan32(50),
        IntendedDisplayTime: new TickCount64(40),
        CpuStartTime: new TickCount64(60)
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
      var bytes = new MarkerPayload(MarkerKind.Frame, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(2)).Encode(
        StartMetadata.FromTag(5, "ignored")
      );
      Assert.That(bytes, Has.Length.EqualTo(57), "a frame marker's payload");
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new MarkerPayload(MarkerKind.Frame, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(2)).Encode();
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
      var bytes = new MarkerPayload(MarkerKind.SequenceStart, 3, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new TimeSpan(2)).Encode(
        StartMetadata.FromTag(0, "ab")
      );
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.True);
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _), Is.False);
      Assert.That(MarkerPayload.TryDecode([.. bytes, 0], out _), Is.False);
    }
  }
}
