//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Settings files keep their earlier versions: a replaced or deleted file ends up in the backup folder, identical saves add nothing and old backups
//* are pruned.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class SettingsFileTests
  {
    private static string[] Backups(TempDirectory directory, string name) =>
      Directory.Exists(Path.Combine(directory.Path, SettingsFile.BackupDirectoryName))
        ? Directory.GetFiles(Path.Combine(directory.Path, SettingsFile.BackupDirectoryName), name + ".*.bak")
        : System.Array.Empty<string>();

    [Test]
    public void Write_NewFile_NoBackup()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");

      Assert.That(SettingsFile.Write(path, "first"), Is.Null);

      Assert.That(File.ReadAllText(path), Is.EqualTo("first"));
      Assert.That(Backups(directory, "settings.json"), Is.Empty);
      Assert.That(File.Exists(path + ".tmp"), Is.False);
    }

    [Test]
    public void Write_Changed_KeepsThePreviousVersion()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, "first");

      var backup = SettingsFile.Write(path, "second");

      Assert.That(File.ReadAllText(path), Is.EqualTo("second"));
      Assert.That(backup, Is.Not.Null);
      Assert.That(File.ReadAllText(backup!), Is.EqualTo("first"));
      Assert.That(Backups(directory, "settings.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public void Write_Unchanged_AddsNoBackup()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, "same");

      Assert.That(SettingsFile.Write(path, "same"), Is.Null);
      Assert.That(Backups(directory, "settings.json"), Is.Empty);
    }

    [Test]
    public void Write_ManyVersions_KeepsTheNewestBackups()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      for (int i = 0; i < SettingsFile.KeepBackups + 5; ++i)
        SettingsFile.Write(path, "version " + i);

      var backups = Backups(directory, "settings.json");

      Assert.That(backups, Has.Length.EqualTo(SettingsFile.KeepBackups));
      // The oldest versions went first; the newest backup is the version before the current one
      var contents = backups.Select(File.ReadAllText).ToList();
      Assert.That(contents, Does.Not.Contain("version 0"));
      Assert.That(contents, Does.Contain("version " + (SettingsFile.KeepBackups + 3)));
    }

    [Test]
    public void Delete_MovesTheFileToTheBackupFolder()
    {
      using var directory = new TempDirectory();
      var path = directory.File("desk.camera-rig.json");
      SettingsFile.Write(path, "rig");

      var backup = SettingsFile.Delete(path);

      Assert.That(File.Exists(path), Is.False);
      Assert.That(File.ReadAllText(backup), Is.EqualTo("rig"));
      Assert.That(Path.GetDirectoryName(backup), Is.EqualTo(SettingsFile.BackupDirectory(path)));
    }

    [Test]
    public void Delete_Missing_Throws()
    {
      using var directory = new TempDirectory();

      Assert.Throws<FileNotFoundException>(() => SettingsFile.Delete(directory.File("missing.json")));
    }
  }
}
