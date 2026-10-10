//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How the trace processor is found: an explicit path, the environment variable, the configuration file, and the newest copy in a folder
//* as Perfetto's Python package keeps them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using MB.FramePacing.Capture;
using NUnit.Framework;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  [NonParallelizable]
  public class TraceProcessorLocatorTests
  {
    private string? m_previous;

    [SetUp]
    public void ClearTheVariable()
    {
      m_previous = Environment.GetEnvironmentVariable(TraceProcessorLocator.EnvironmentVariable);
      Environment.SetEnvironmentVariable(TraceProcessorLocator.EnvironmentVariable, null);
    }

    [TearDown]
    public void RestoreTheVariable() => Environment.SetEnvironmentVariable(TraceProcessorLocator.EnvironmentVariable, m_previous);

    [Test]
    public void TheExplicitPath_ThenTheVariable_ThenTheConfiguration()
    {
      using var temp = new TempFolder();
      string Made(string name)
      {
        string path = temp.File(name);
        File.WriteAllText(path, string.Empty);
        return path;
      }
      var config = new FramePacingConfig { TraceProcessorPath = Made("configured") };

      Assert.That(TraceProcessorLocator.Find(null, config), Is.EqualTo(Path.GetFullPath(config.TraceProcessorPath)));

      string fromEnvironment = Made("from-environment");
      Environment.SetEnvironmentVariable(TraceProcessorLocator.EnvironmentVariable, fromEnvironment);
      Assert.That(TraceProcessorLocator.Find(null, config), Is.EqualTo(Path.GetFullPath(fromEnvironment)));

      string given = Made("given");
      Assert.That(TraceProcessorLocator.Find(given, config), Is.EqualTo(Path.GetFullPath(given)));
    }

    [Test]
    public void APathThatIsNotThere_IsRefusedWithWhereItCameFrom()
    {
      using var temp = new TempFolder();
      string missing = temp.File("nope");

      Assert.That(Assert.Throws<FileNotFoundException>(() => TraceProcessorLocator.Find(missing))!.Message, Does.Contain("--trace-processor"));
      var config = new FramePacingConfig { TraceProcessorPath = missing };
      Assert.That(Assert.Throws<FileNotFoundException>(() => TraceProcessorLocator.Find(null, config))!.Message, Does.Contain("traceProcessorPath"));
      Environment.SetEnvironmentVariable(TraceProcessorLocator.EnvironmentVariable, missing);
      Assert.That(
        Assert.Throws<FileNotFoundException>(() => TraceProcessorLocator.Find(null, config))!.Message,
        Does.Contain(TraceProcessorLocator.EnvironmentVariable)
      );
    }

    [Test]
    public void TheNewestCopyOfAFolder_IsTaken()
    {
      using var temp = new TempFolder();
      string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
      Assert.That(TraceProcessorLocator.TryFindIn(temp.File("no-such-folder")), Is.Null);
      Assert.That(TraceProcessorLocator.TryFindIn(temp.Path), Is.Null);

      string older = temp.File("trace_processor_shell-aaaa" + extension);
      string newer = temp.File("trace_processor_shell-bbbb" + extension);
      File.WriteAllText(older, string.Empty);
      File.WriteAllText(newer, string.Empty);
      File.WriteAllText(temp.File("trace_processor_shell-cccc.partial"), string.Empty);
      File.WriteAllText(temp.File("other" + extension), string.Empty);
      File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
      File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

      Assert.That(TraceProcessorLocator.TryFindIn(temp.Path), Is.EqualTo(newer));
    }

    [Test]
    public void TheConfiguration_KeepsThePath()
    {
      using var temp = new TempFolder();
      string path = temp.File("mb-framepacing.json");

      new FramePacingConfig { TraceProcessorPath = "/opt/perfetto/trace_processor_shell" }.Save(path);

      Assert.That(File.ReadAllText(path), Does.Contain("\"traceProcessorPath\""));
      Assert.That(FramePacingConfig.Load(path).TraceProcessorPath, Is.EqualTo("/opt/perfetto/trace_processor_shell"));
      Assert.That(TraceProcessorLocator.DownloadPage, Does.StartWith("https://"));
      Assert.That(TraceProcessorLocator.PackageCacheDirectory, Does.EndWith("prebuilts"));
    }
  }
}
