//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A QR module matrix. IsDark is true for dark modules.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>A QR module matrix. <see cref="IsDark"/> is true for dark modules.</summary>
  public sealed class ModuleMatrix
  {
    private readonly bool[] m_modules;

    public ModuleMatrix(int size)
    {
      Size = size;
      m_modules = new bool[size * size];
    }

    public int Size { get; }

    public bool IsDark(int x, int y) => m_modules[(y * Size) + x];

    internal void Set(int x, int y, bool dark) => m_modules[(y * Size) + x] = dark;
  }
}
