//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* mb-framepacing.json: template, round trip and how it takes part in finding ffmpeg.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using MB.FramePacing.Capture.Ffmpeg;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  [NonParallelizable]
  public class ConfigTests
  {
    [Test]
    public void Template_IsValidJson_WithEverythingCommentedOut()
    {
      using var temp = new TempDirectory();
      var path = FramePacingConfig.EnsureExists(temp.File("mb-framepacing.json"));
      Assert.That(File.ReadAllText(path), Is.EqualTo(FramePacingConfig.Template));

      var config = FramePacingConfig.Load(path);
      Assert.That(config.FfmpegPath, Is.Null);
      Assert.That(config.CaptureDirectory, Is.Null);
      Assert.That(config.SourcePath, Is.EqualTo(path));
    }

    [Test]
    public void EnsureExists_KeepsAnExistingFile()
    {
      using var temp = new TempDirectory();
      var path = temp.File("mb-framepacing.json");
      File.WriteAllText(path, "{ \"captureDirectory\": \"x\" }");
      FramePacingConfig.EnsureExists(path);
      Assert.That(FramePacingConfig.Load(path).CaptureDirectory, Is.EqualTo("x"));
    }

    [Test]
    public void FormatVersion_WrittenAndAFileWithoutItCountsAsTheFirst()
    {
      using var temp = new TempDirectory();
      var path = temp.File("mb-framepacing.json");
      File.WriteAllText(path, "{ \"captureDirectory\": \"x\" }");

      Assert.That(FramePacingConfig.Load(path).FormatVersion, Is.EqualTo(1));
      FramePacingConfig.Load(path).Save(path);
      Assert.That(File.ReadAllText(path), Does.Contain($"\"formatVersion\": {FramePacingConfig.CurrentFormatVersion}"));
    }

    [Test]
    public void FormatVersion_Newer_IsRefused()
    {
      using var temp = new TempDirectory();
      var path = temp.File("mb-framepacing.json");
      File.WriteAllText(path, $"{{ \"formatVersion\": {FramePacingConfig.CurrentFormatVersion + 1} }}");

      var error = Assert.Throws<InvalidDataException>(() => FramePacingConfig.Load(path));

      Assert.That(error!.Message, Does.Contain("newer"));
    }

    [Test]
    public void InvalidFile_ErrorNamesTheBackup()
    {
      using var temp = new TempDirectory();
      var path = temp.File("mb-framepacing.json");
      new FramePacingConfig { CaptureDirectory = "x" }.Save(path);
      new FramePacingConfig { CaptureDirectory = "y" }.Save(path);
      File.WriteAllText(path, "{ broken");

      var error = Assert.Throws<InvalidDataException>(() => FramePacingConfig.Load(path));

      Assert.That(error!.Message, Does.Contain(SettingsFile.PreviousPath(path)));
    }

    [Test]
    public void SaveAndLoad_RoundTrip()
    {
      using var temp = new TempDirectory();
      var path = temp.File("sub/mb-framepacing.json");
      var written = new FramePacingConfig { FfmpegPath = @"C:\tools\ffmpeg.exe", CaptureDirectory = @"D:\captures" }.Save(path);

      var loaded = FramePacingConfig.Load(written);
      Assert.That(loaded.FfmpegPath, Is.EqualTo(@"C:\tools\ffmpeg.exe"));
      Assert.That(loaded.CaptureDirectory, Is.EqualTo(@"D:\captures"));
      Assert.That(File.ReadAllText(written), Does.Contain("\"ffmpegPath\""));
    }

    [Test]
    public void Load_ExplicitMissingFile_Throws_InvalidJson_IsReported()
    {
      using var temp = new TempDirectory();
      Assert.Throws<FileNotFoundException>(() => FramePacingConfig.Load(temp.File("missing.json")));
      var bad = temp.File("bad.json");
      File.WriteAllText(bad, "{ ffmpegPath: ");
      Assert.Throws<InvalidDataException>(() => FramePacingConfig.Load(bad));
    }

    [Test]
    public void FfmpegLocator_UsesTheConfiguredPath_AfterTheEnvironmentVariable()
    {
      using var temp = new TempDirectory();
      var configured = temp.File(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
      File.WriteAllText(configured, string.Empty);
      var config = new FramePacingConfig { FfmpegPath = configured };

      var previous = Environment.GetEnvironmentVariable(FfmpegLocator.EnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(FfmpegLocator.EnvironmentVariable, null);
        Assert.That(FfmpegLocator.Find(null, config), Is.EqualTo(Path.GetFullPath(configured)));

        var fromEnvironment = temp.File("env-ffmpeg");
        File.WriteAllText(fromEnvironment, string.Empty);
        Environment.SetEnvironmentVariable(FfmpegLocator.EnvironmentVariable, fromEnvironment);
        Assert.That(FfmpegLocator.Find(null, config), Is.EqualTo(Path.GetFullPath(fromEnvironment)));
      }
      finally
      {
        Environment.SetEnvironmentVariable(FfmpegLocator.EnvironmentVariable, previous);
      }
    }

    [Test]
    public void FfmpegLocator_MissingConfiguredPath_Throws()
    {
      using var temp = new TempDirectory();
      var previous = Environment.GetEnvironmentVariable(FfmpegLocator.EnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(FfmpegLocator.EnvironmentVariable, null);
        var ex = Assert.Throws<FileNotFoundException>(() => FfmpegLocator.Find(null, new FramePacingConfig { FfmpegPath = temp.File("nope.exe") }));
        Assert.That(ex!.Message, Does.Contain("ffmpegPath"));
      }
      finally
      {
        Environment.SetEnvironmentVariable(FfmpegLocator.EnvironmentVariable, previous);
      }
    }

    [Test]
    public void DownloadPage_PointsAtFfmpegOrg()
    {
      Assert.That(FfmpegLocator.DownloadPage, Does.StartWith("https://ffmpeg.org/download.html#build-"));
    }
  }
}
