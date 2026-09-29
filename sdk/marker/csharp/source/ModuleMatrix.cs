//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The encoded marker: the QR symbol's modules, 1 bit each (1 = dark), packed row-major, most significant bit first, continuous across rows, the
//* last byte zero padded (exactly test-data/markers/modules.csv's modulesHex). MarkerGenerator.TryGenerateModules writes it into the caller's
//* bytes once per marker; every drawing output (Marker.ModulesToQuads, ModulesToTriangles, ModulesToIndexed, ModulesToBitmap) is made from it.
//*
//* A view over bytes the caller owns (a stackalloc, or a reused byte[] of Marker.MaxPackedModuleByteCount), so it never allocates. C# 9 has no
//* safe inline buffer, so it is a ref struct: pass it on, but keep the bytes, not the view, in a field.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly ref struct ModuleMatrix
  {
    private readonly ReadOnlySpan<byte> m_bits;

    private ModuleMatrix(int size, ReadOnlySpan<byte> bits)
    {
      Size = size;
      m_bits = bits;
    }

    /// <summary>Modules per side: 41 for the main marker, 25 for the sync marker, 0 for an empty (default) matrix.</summary>
    public int Size { get; }

    /// <summary>The packed bits: <see cref="Marker.PackedModuleByteCount"/>(<see cref="Size"/>) bytes.</summary>
    public ReadOnlySpan<byte> Bits => m_bits;

    public bool IsEmpty => Size == 0;

    public bool IsDark(int x, int y)
    {
      int index = (y * Size) + x;
      return ((m_bits[index >> 3] >> (7 - (index & 7))) & 1) != 0;
    }

    /// <summary>
    /// A view of <paramref name="size"/> x <paramref name="size"/> modules over packed bits (at least <see cref="Marker.PackedModuleByteCount"/>
    /// bytes; bits past the last module are ignored). False for a size that is not a QR symbol's (21 to 41, in steps of 4) or too few bytes.
    /// </summary>
    public static bool TryFromBits(int size, ReadOnlySpan<byte> bits, out ModuleMatrix matrix)
    {
      int byteCount = Marker.PackedModuleByteCount(size);
      if (size < 21 || size > Marker.QrModuleCount || (size - 17) % 4 != 0 || bits.Length < byteCount)
      {
        matrix = default;
        return false;
      }
      matrix = new ModuleMatrix(size, bits.Slice(0, byteCount));
      return true;
    }

    /// <summary>The same size and modules (the padding bits of the last byte are not compared).</summary>
    public bool SequenceEqual(ModuleMatrix other)
    {
      if (Size != other.Size)
        return false;
      if (Size == 0)
        return true;
      int last = m_bits.Length - 1;
      int usedBits = (Size * Size) % 8;
      int mask = usedBits == 0 ? 0xFF : (0xFF << (8 - usedBits)) & 0xFF;
      return m_bits.Slice(0, last).SequenceEqual(other.m_bits.Slice(0, last)) && (m_bits[last] & mask) == (other.m_bits[last] & mask);
    }
  }
}
