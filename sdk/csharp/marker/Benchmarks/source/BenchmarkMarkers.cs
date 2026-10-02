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
    private static readonly TimeSpan32 g_frameTime = new TimeSpan32(166_667);

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
      long offset = (long)frameIndex * g_frameTime.Ticks;
      return new Payload(
        MarkerKind.Frame,
        0x12345678,
        frameIndex,
        MarkerFlags.NoFlags,
        new TimeSpan(offset),
        g_frameTime,
        g_frameTime,
        new TickCount64(36_000_000_000 + offset),
        new TickCount64(35_999_900_000 + offset),
        new TimeSpan32(80_000)
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
