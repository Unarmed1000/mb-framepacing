//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds the pacer's golden data (test-data/pacer, written by pacer-sim with tools/update_pacer_test_data.py) by walking up from the test directory.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.IO;

namespace MB.FramePacing.Pacer.UnitTest
{
  internal static class TestData
  {
    /// <summary>test-data/pacer: every golden scenario's frames and results.</summary>
    public static string PacerDirectory
    {
      get
      {
        var directory = new DirectoryInfo(NUnit.Framework.TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "pacer", "60-busy-frames.csv")))
          directory = directory.Parent;
        return directory != null
          ? Path.Combine(directory.FullName, "test-data", "pacer")
          : throw new DirectoryNotFoundException("test-data/pacer not found above the test directory");
      }
    }
  }
}
