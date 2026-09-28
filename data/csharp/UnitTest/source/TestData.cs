//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds the golden data (test-data/data, written by the tools with tools/update_test_data.py) by walking up from the test directory.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.IO;

namespace MB.FramePacing.Data.UnitTest
{
  internal static class TestData
  {
    /// <summary>The test clip the golden data was made from.</summary>
    public const string Clip = "60-busy-full-rate";

    /// <summary>test-data/data/&lt;clip&gt;: capture.json, captures.mbcd, analysis/ and digest.json.</summary>
    public static string ClipDirectory => Path.Combine(FindRoot(), "test-data", "data", Clip);

    public static string AnalysisDirectory => Path.Combine(ClipDirectory, AnalysisFiles.DirectoryName);

    public static string DigestPath => Path.Combine(ClipDirectory, "digest.json");

    /// <summary>The file with its line endings as LF: the tools write the platform's, and git may convert them.</summary>
    public static string ReadText(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string FindRoot()
    {
      var directory = new DirectoryInfo(NUnit.Framework.TestContext.CurrentContext.TestDirectory);
      while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "test-data", "data")))
        directory = directory.Parent;
      return directory?.FullName ?? throw new DirectoryNotFoundException("test-data/data not found above the test directory");
    }
  }
}
