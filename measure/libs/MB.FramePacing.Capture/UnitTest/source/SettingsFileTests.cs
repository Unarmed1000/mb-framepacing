//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Settings files keep the old version: the file a save replaces (or a delete removes) is kept as <file>.bak, the last file in a format as
//* <file>.v<N>.bak when a save changes the format, identical saves change nothing and a failed save leaves the file alone.
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
    private static string[] BackupFiles(TempDirectory directory) =>
      Directory.Exists(Path.Combine(directory.Path, SettingsFile.BackupDirectoryName))
        ? Directory.GetFiles(Path.Combine(directory.Path, SettingsFile.BackupDirectoryName)).Select(p => Path.GetFileName(p)!).Order().ToArray()
        : System.Array.Empty<string>();

    private static string Json(int version, string value) => $"{{ \"formatVersion\": {version}, \"value\": \"{value}\" }}";

    [Test]
    public void Write_NewFile_NoBackup()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");

      Assert.That(SettingsFile.Write(path, Json(1, "first"), 1), Is.Null);

      Assert.That(File.ReadAllText(path), Is.EqualTo(Json(1, "first")));
      Assert.That(BackupFiles(directory), Is.Empty);
    }

    [Test]
    public void Write_Changed_KeepsOnlyTheLastReplacedVersion()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, Json(1, "first"), 1);
      SettingsFile.Write(path, Json(1, "second"), 1);

      var backup = SettingsFile.Write(path, Json(1, "third"), 1);

      Assert.That(File.ReadAllText(path), Is.EqualTo(Json(1, "third")));
      Assert.That(backup, Is.EqualTo(SettingsFile.PreviousPath(path)));
      Assert.That(File.ReadAllText(backup!), Is.EqualTo(Json(1, "second")));
      Assert.That(BackupFiles(directory), Is.EqualTo(new[] { "settings.json.bak" }));
    }

    [Test]
    public void Write_HandEditedFile_TheEditIsBackedUp()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, Json(1, "saved"), 1);
      File.WriteAllText(path, Json(1, "edited by hand"));

      SettingsFile.Write(path, Json(1, "saved again"), 1);

      Assert.That(File.ReadAllText(SettingsFile.PreviousPath(path)), Is.EqualTo(Json(1, "edited by hand")));
    }

    [Test]
    public void Write_Unchanged_ChangesNothing()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, Json(1, "same"), 1);

      Assert.That(SettingsFile.Write(path, Json(1, "same"), 1), Is.Null);
      Assert.That(BackupFiles(directory), Is.Empty);
    }

    [Test]
    public void Write_FormatChange_KeepsTheLastFileOfTheOldFormat()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, Json(1, "v1 first"), 1);
      SettingsFile.Write(path, Json(1, "v1 last"), 1);

      SettingsFile.Write(path, Json(2, "v2 first"), 2);
      SettingsFile.Write(path, Json(2, "v2 second"), 2);

      Assert.That(File.ReadAllText(SettingsFile.VersionPath(path, 1)), Is.EqualTo(Json(1, "v1 last")));
      Assert.That(File.ReadAllText(SettingsFile.PreviousPath(path)), Is.EqualTo(Json(2, "v2 first")));
      Assert.That(BackupFiles(directory), Is.EqualTo(new[] { "settings.json.bak", "settings.json.v1.bak" }));
    }

    [Test]
    public void Write_FileWithoutAVersion_CountsAsFormat1()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      File.WriteAllText(path, "{ // written before files had a version\n \"value\": \"old\" }");

      SettingsFile.Write(path, Json(2, "new"), 2);

      Assert.That(File.ReadAllText(SettingsFile.VersionPath(path, 1)), Does.Contain("old"));
    }

    [Test]
    public void Write_LeavesNoTemporaryFiles()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");

      SettingsFile.Write(path, Json(1, "first"), 1);
      SettingsFile.Write(path, Json(2, "second"), 2);

      Assert.That(Directory.GetFiles(directory.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "settings.json" }));
      Assert.That(BackupFiles(directory), Is.EqualTo(new[] { "settings.json.bak", "settings.json.v1.bak" }));
    }

    [Test]
    public void Write_Fails_KeepsTheFileAndLeavesNoTemporaryFile()
    {
      if (!System.OperatingSystem.IsWindows())
        Assert.Ignore("Only Windows refuses to replace a file another program holds open");
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      SettingsFile.Write(path, Json(1, "first"), 1);

      using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        Assert.Catch(() => SettingsFile.Write(path, Json(1, "second"), 1));

      Assert.That(File.ReadAllText(path), Is.EqualTo(Json(1, "first")));
      Assert.That(Directory.GetFiles(directory.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "settings.json" }));
    }

    [Test]
    public void BackupHint_NamesThePreviousVersion()
    {
      using var directory = new TempDirectory();
      var path = directory.File("settings.json");
      Assert.That(SettingsFile.BackupHint(path), Is.Empty);
      SettingsFile.Write(path, Json(1, "first"), 1);
      SettingsFile.Write(path, Json(1, "second"), 1);

      Assert.That(SettingsFile.BackupHint(path), Does.Contain(SettingsFile.PreviousPath(path)));
    }

    [Test]
    public void Delete_KeepsTheFileAsThePreviousVersion()
    {
      using var directory = new TempDirectory();
      var path = directory.File("desk.camera-rig.json");
      SettingsFile.Write(path, Json(1, "rig"), 1);

      var backup = SettingsFile.Delete(path);

      Assert.That(File.Exists(path), Is.False);
      Assert.That(backup, Is.EqualTo(SettingsFile.PreviousPath(path)));
      Assert.That(File.ReadAllText(backup), Is.EqualTo(Json(1, "rig")));
    }

    [Test]
    public void Delete_Missing_Throws()
    {
      using var directory = new TempDirectory();

      Assert.Throws<FileNotFoundException>(() => SettingsFile.Delete(directory.File("missing.json")));
    }

    [TestCase("{ \"formatVersion\": 3 }", 3)]
    [TestCase("{ \"FormatVersion\": 2 }", 2)]
    [TestCase("{ // comment\n \"value\": 1, }", 1)]
    [TestCase("{ broken", null)]
    [TestCase("[]", null)]
    public void ReadFormatVersion(string json, int? expected) =>
      Assert.That(SettingsFile.ReadFormatVersion(System.Text.Encoding.UTF8.GetBytes(json)), Is.EqualTo(expected));
  }
}
