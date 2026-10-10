//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A folder of a test's own under the system's temporary folder, deleted when the test is done.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  public sealed class TempFolder : IDisposable
  {
    public TempFolder()
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
      catch (IOException)
      {
        // A file still in use: the system's temporary folder is cleaned by the system
      }
    }
  }
}
