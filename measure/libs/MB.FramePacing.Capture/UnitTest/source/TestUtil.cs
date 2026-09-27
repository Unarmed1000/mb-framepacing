//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Temporary directories for capture tests.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;

namespace MB.FramePacing.Capture.UnitTest
{
  public sealed class TempDirectory : IDisposable
  {
    public TempDirectory()
    {
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
      try
      {
        Directory.Delete(Path, recursive: true);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
