//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A QR module matrix. IsDark is true for dark modules.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
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
