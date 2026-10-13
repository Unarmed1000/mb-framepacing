//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The encoded marker: the QR symbol's modules, 1 bit each (1 = dark), packed row-major, most significant bit first, continuous across rows, the
//* last byte zero padded (exactly test-data/markers/modules.csv's modulesHex). MarkerGenerator.TryGenerateModules writes it into the caller's
//* bytes once per marker; every drawing output (FrameMarker.ModulesToQuads, ModulesToTriangles, ModulesToIndexed, ModulesToBitmap) is made from it.
//*
//* A view over bytes the caller owns (a stackalloc, or a reused byte[] of MaxPackedModuleByteCount), so it never allocates. C# 9 has no
//* safe inline buffer, so it is a ref struct: pass it on, but keep the bytes, not the view, in a field.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly ref struct ModuleMatrix
  {
    /// <summary>Modules per side of the main marker's symbol (frame, start and end: QR version 6).</summary>
    public const int MainSize = 41;

    /// <summary>Modules per side of the sync marker's symbol (QR version 2).</summary>
    public const int SyncSize = 25;

    /// <summary>The packed bytes of the largest matrix, the main marker's: 211. A buffer of this size fits every marker.</summary>
    public const int MaxPackedModuleByteCount = ((MainSize * MainSize) + 7) / 8;

    private ModuleMatrix(int size, ReadOnlySpan<byte> bits)
    {
      Size = size;
      Bits = bits;
    }

    /// <summary>Modules per side: 41 for the main marker, 25 for the sync marker, 0 for an empty (default) matrix.</summary>
    public readonly int Size;

    /// <summary>The packed bits: <see cref="PackedModuleByteCount"/>(<see cref="Size"/>) bytes.</summary>
    public readonly ReadOnlySpan<byte> Bits;

    public bool IsEmpty => Size == 0;

    /// <summary>Modules per side of a marker kind's symbol: the main marker (frame, start and end) or the smaller sync marker.</summary>
    public static int SizeFor(MarkerKind kind) => kind == MarkerKind.Sync ? SyncSize : MainSize;

    /// <summary>The bytes of a packed matrix of <paramref name="size"/> x <paramref name="size"/> modules (79 for the sync marker's 25).</summary>
    public static int PackedModuleByteCount(int size) => size <= 0 ? 0 : ((size * size) + 7) / 8;

    /// <summary>
    /// Whether the module in column <paramref name="x"/> of row <paramref name="y"/> is dark. Both must be 0 to <see cref="Size"/> - 1:
    /// there is no module outside (a column past the row's end reads the next row's, a row past the last throws).
    /// </summary>
    public bool IsDark(int x, int y)
    {
      int index = (y * Size) + x;
      return ((Bits[index >> 3] >> (7 - (index & 7))) & 1) != 0;
    }

    /// <summary>
    /// A view of <paramref name="size"/> x <paramref name="size"/> modules over packed bits (at least <see cref="PackedModuleByteCount"/>
    /// bytes; bits past the last module are ignored). False for a size that is not a marker's (<see cref="MainSize"/> or
    /// <see cref="SyncSize"/>: the sizes the drawing functions and the grid know) or too few bytes.
    /// </summary>
    public static bool TryFromBits(int size, ReadOnlySpan<byte> bits, out ModuleMatrix matrix)
    {
      int byteCount = PackedModuleByteCount(size);
      if ((size != MainSize && size != SyncSize) || bits.Length < byteCount)
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
      int last = Bits.Length - 1;
      // Both sizes are odd and an odd square is 1 more than a multiple of 8: the last byte holds 1 module and 7 bits of padding
      return Bits.Slice(0, last).SequenceEqual(other.Bits.Slice(0, last)) && (Bits[last] & 0x80) == (other.Bits[last] & 0x80);
    }
  }
}
