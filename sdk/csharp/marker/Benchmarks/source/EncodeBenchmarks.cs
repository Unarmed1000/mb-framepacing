//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Encoding: the payload's bytes, and the QR symbol (TryGenerateModules) every frame starts from. The frame index changes every
//* iteration, as it does every frame, so the QR encoder picks its mask for a new symbol each time.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;

namespace MB.FramePacing.Marker.Benchmarks
{
  [MemoryDiagnoser]
  public class EncodeBenchmarks
  {
    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly byte[] m_bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private readonly byte[] m_payloadBytes = new byte[Payload.MaxEncodedByteCount];
    private readonly StartMetadata m_metadata = BenchmarkMarkers.Metadata;
    private ulong m_frameIndex;

    [Params(MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.Sync)]
    public MarkerKind Kind { get; set; }

    [Benchmark]
    public int EncodePayload() => FrameMarker.EncodePayload(BenchmarkMarkers.FramePayload(m_frameIndex++).WithKind(Kind), m_metadata, m_payloadBytes);

    [Benchmark]
    public int GenerateModules() =>
      m_generator.TryGenerateModules(BenchmarkMarkers.FramePayload(m_frameIndex++).WithKind(Kind), m_metadata, m_bits, out var matrix)
        ? matrix.Size
        : 0;
  }
}
