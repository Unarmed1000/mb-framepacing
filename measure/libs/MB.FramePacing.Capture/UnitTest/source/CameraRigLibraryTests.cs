//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saving, listing, resolving and deleting calibrated cameras by name.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class CameraRigLibraryTests
  {
    private static CameraRig Rig() =>
      new CameraRig
      {
        CameraWidth = 640,
        CameraHeight = 360,
        CameraFps = 330,
        Zones = new[] { new CameraZone(new Homography(4, 0, 10, 0, 4, 20, 0, 0), 4, 10, 220, 0, 0.9) },
      };

    [Test]
    public void SaveListResolveDelete()
    {
      using var directory = new TempDirectory();

      var path = CameraRigLibrary.Save(Rig(), "desk 27in", directory.Path);
      CameraRigLibrary.Save(Rig(), "bench", directory.Path);

      Assert.That(CameraRigLibrary.List(directory.Path).Select(r => r.Name), Is.EqualTo(new[] { "bench", "desk 27in" }));
      Assert.That(CameraRigLibrary.Resolve("desk 27in", directory.Path), Is.EqualTo(path));
      Assert.That(CameraRigLibrary.Resolve(path, directory.Path), Is.EqualTo(Path.GetFullPath(path)));
      Assert.That(CameraRigLibrary.Load("desk 27in", directory.Path).Name, Is.EqualTo("desk 27in"));

      var backup = CameraRigLibrary.Delete("bench", directory.Path);
      Assert.That(CameraRigLibrary.List(directory.Path).Select(r => r.Name), Is.EqualTo(new[] { "desk 27in" }));
      // The deleted camera is kept in the backup folder, which the library does not list
      Assert.That(CameraRig.Load(backup).Name, Is.EqualTo("bench"));
    }

    [Test]
    public void Save_ReplacingACamera_KeepsThePreviousVersion()
    {
      using var directory = new TempDirectory();
      CameraRigLibrary.Save(Rig() with { CameraFps = 240 }, "desk", directory.Path);

      CameraRigLibrary.Save(Rig() with { CameraFps = 330 }, "desk", directory.Path);

      Assert.That(CameraRigLibrary.Load("desk", directory.Path).CameraFps, Is.EqualTo(330));
      var backups = Directory.GetFiles(Path.Combine(directory.Path, SettingsFile.BackupDirectoryName));
      Assert.That(backups.Select(p => CameraRig.Load(p).CameraFps), Is.EqualTo(new[] { 240.0 }));
    }

    [Test]
    public void Resolve_UnknownName_ListsTheSavedCameras()
    {
      using var directory = new TempDirectory();
      CameraRigLibrary.Save(Rig(), "desk", directory.Path);

      var error = Assert.Throws<FileNotFoundException>(() => CameraRigLibrary.Resolve("lab", directory.Path));

      Assert.That(error!.Message, Does.Contain("desk"));
    }

    [Test]
    public void List_ReportsUnreadableFiles()
    {
      using var directory = new TempDirectory();
      File.WriteAllText(directory.File("broken" + CameraRig.FileExtension), "{ not json");

      var entry = CameraRigLibrary.List(directory.Path).Single();

      Assert.That(entry.Rig, Is.Null);
      Assert.That(entry.Error, Is.Not.Empty);
    }

    [TestCase("desk", true)]
    [TestCase("desk 27in_v2.1", true)]
    [TestCase("", false)]
    [TestCase(".hidden", false)]
    [TestCase("a/b", false)]
    [TestCase("a:b", false)]
    public void IsValidName(string name, bool valid) => Assert.That(CameraRigLibrary.IsValidName(name), Is.EqualTo(valid));
  }
}
