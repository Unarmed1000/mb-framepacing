//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Encodes a marker: the payload's QR symbol as a packed module matrix (ModuleMatrix), the one step every drawing output starts from. Draw
//* it with FrameMarker.ModulesToQuads, ModulesToTriangles, ModulesToIndexed or ModulesToBitmap; one matrix can feed several.
//*
//* Create one generator and reuse it every frame: the QR encoder's buffers are allocated in the constructor (the payload bytes live on the
//* stack, the matrix in the caller's bytes), so encoding never allocates. Not thread-safe; use one generator per thread.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;

namespace MB.FramePacing.Marker
{
  public sealed class MarkerGenerator
  {
    private readonly QrEncoder m_encoder = new QrEncoder();

    /// <summary>
    /// Encode the payload's QR symbol into <paramref name="destination"/> (at least <see cref="ModuleMatrix.PackedModuleByteCount"/> of its size;
    /// <see cref="ModuleMatrix.MaxPackedModuleByteCount"/> fits every marker) and view it as <paramref name="matrix"/>. The metadata is only used by
    /// start markers. Returns false (an empty matrix) if the destination is too small.
    /// </summary>
    public bool TryGenerateModules(in Payload payload, in StartMetadata metadata, Span<byte> destination, out ModuleMatrix matrix)
    {
      matrix = default;
      Span<byte> payloadBytes = stackalloc byte[Payload.MaxEncodedByteCount];
      // The buffer fits every payload, and a Payload's kind is always a MarkerKind: this cannot fail
      int byteCount = FrameMarker.EncodePayload(payload, metadata, payloadBytes);
      // Every kind is pinned to one version, so the symbol never changes size between frames. Its bytes fit that version
      // (WireFormat.QrCapacityBytes, SyncQrCapacityBytes), so the encoder cannot fail either
      int version = payload.Kind == MarkerKind.Sync ? WireFormat.SyncQrVersion : WireFormat.QrVersion;
      bool encoded = m_encoder.Encode(payloadBytes.Slice(0, byteCount), version, version);
      Debug.Assert(encoded, "every kind's payload fits its QR version");

      int size = m_encoder.Size;
      int packedCount = ModuleMatrix.PackedModuleByteCount(size);
      if (destination.Length < packedCount)
        return false;
      // Pack the symbol: row-major, most significant bit first, continuous across rows
      var bits = destination.Slice(0, packedCount);
      bits.Clear();
      int index = 0;
      for (int y = 0; y < size; ++y)
      {
        for (int x = 0; x < size; ++x, ++index)
        {
          if (m_encoder.IsDark(x, y))
            bits[index >> 3] |= (byte)(0x80 >> (index & 7));
        }
      }
      return ModuleMatrix.TryFromBits(size, bits, out matrix);
    }

    /// <summary>Encode a frame, end or sync marker (see <see cref="TryGenerateModules(in Payload, in StartMetadata, Span{byte}, out ModuleMatrix)"/>).</summary>
    public bool TryGenerateModules(in Payload payload, Span<byte> destination, out ModuleMatrix matrix) =>
      TryGenerateModules(payload, default, destination, out matrix);
  }
}
