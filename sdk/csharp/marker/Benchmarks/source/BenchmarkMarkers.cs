//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The markers the benchmarks measure: a frame marker with every field set, as a paced application writes it every frame (the C++
//* benchmarks' BenchmarkMarkers).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker.Benchmarks
{
  internal static class BenchmarkMarkers
  {
    private static readonly NanosecondTimeDuration g_frameTime = NanosecondTimeDuration.FromNanoseconds(16_666_667);

    /// <summary>A start marker's metadata: a start time and a text sequence id.</summary>
    public static StartMetadata Metadata
    {
      get
      {
        if (!SequenceId.TryFromText("benchmark-run", out var id))
          throw new InvalidOperationException("The benchmark's sequence id is not valid");
        return new StartMetadata(639_257_616_000_000_000, id);
      }
    }

    /// <summary>Frame <paramref name="frameIndex"/> of a 60 fps run: every field set, the times advancing one frame time per frame.</summary>
    public static Payload FramePayload(ulong frameIndex)
    {
      long offset = (long)frameIndex * g_frameTime.Nanoseconds;
      return new Payload(
        MarkerKind.Frame,
        0x12345678,
        frameIndex,
        MarkerFlags.NoFlags,
        new NanosecondTimeSpan(offset),
        g_frameTime,
        g_frameTime,
        new NanosecondTickCount(3_600_000_000_000 + offset),
        new NanosecondTickCount(3_599_990_000_000 + offset),
        NanosecondTimeDuration.FromNanoseconds(8_000_000)
      );
    }

    /// <summary>
    /// The packed modules of a typical frame of <paramref name="kind"/> (the frame marker's payload, with that kind) in
    /// <paramref name="bits"/>, and its size.
    /// </summary>
    public static int Encode(MarkerKind kind, byte[] bits)
    {
      if (!new MarkerGenerator().TryGenerateModules(FramePayload(1000).WithKind(kind), Metadata, bits, out var matrix))
        throw new InvalidOperationException("TryGenerateModules failed");
      return matrix.Size;
    }
  }
}
