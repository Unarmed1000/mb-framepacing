//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The QR module matrix of a marker (true = dark module). Allocated once for the largest symbol and reused, so filling it never allocates.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public sealed class ModuleMatrix
  {
    private readonly bool[] m_modules = new bool[Marker.QrModuleCount * Marker.QrModuleCount];

    /// <summary>Modules per side (25 for frame and end markers, 29 to 41 for start markers), 0 before the first successful generate.</summary>
    public int Size { get; private set; }

    public bool IsDark(int x, int y) => m_modules[(y * Marker.QrModuleCount) + x];

    internal void CopyFrom(QrEncoder encoder)
    {
      Size = encoder.Size;
      for (int y = 0; y < Size; ++y)
      {
        for (int x = 0; x < Size; ++x)
          m_modules[(y * Marker.QrModuleCount) + x] = encoder.IsDark(x, y);
      }
    }
  }
}
