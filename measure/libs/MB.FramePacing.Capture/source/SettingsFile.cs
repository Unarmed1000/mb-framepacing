//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saves and deletes settings files (the configuration file, the GUI settings, saved cameras) without losing earlier versions: a changed file is
//* written atomically (a temporary file, then a replace), and the version it replaces, or the file being deleted, is kept in a "backup" folder
//* next to it as <file name>.<yyyyMMdd-HHmmss-fff>.bak. The newest KeepBackups versions of each file are kept. Restore one by copying it back.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture
{
  public static class SettingsFile
  {
    public const string BackupDirectoryName = "backup";

    public const int KeepBackups = 20;

    /// <summary>The backup folder for <paramref name="path"/>.</summary>
    public static string BackupDirectory(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, BackupDirectoryName);

    /// <summary>
    /// Write <paramref name="contents"/> to <paramref name="path"/> (creating the folder). When the file exists with other contents, that version is
    /// backed up first. Returns the backup path, or null when nothing was backed up.
    /// </summary>
    public static string? Write(string path, string contents)
    {
      path = Path.GetFullPath(path);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      string? backup = null;
      if (File.Exists(path))
      {
        if (File.ReadAllText(path) == contents)
          return null;
        backup = Backup(path, move: false);
      }
      var temp = path + ".tmp";
      File.WriteAllText(temp, contents);
      File.Move(temp, path, overwrite: true);
      return backup;
    }

    /// <summary>Delete <paramref name="path"/> by moving it to the backup folder. Returns the backup path.</summary>
    public static string Delete(string path)
    {
      path = Path.GetFullPath(path);
      if (!File.Exists(path))
        throw new FileNotFoundException($"'{path}' does not exist", path);
      return Backup(path, move: true);
    }

    private static string Backup(string path, bool move)
    {
      var directory = BackupDirectory(path);
      Directory.CreateDirectory(directory);
      var name = Path.GetFileName(path);
      var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
      var backup = Path.Combine(directory, $"{name}.{stamp}.bak");
      for (int i = 2; File.Exists(backup); ++i)
        backup = Path.Combine(directory, $"{name}.{stamp}-{i}.bak");
      if (move)
        File.Move(path, backup);
      else
        File.Copy(path, backup);
      Prune(directory, name);
      return backup;
    }

    /// <summary>Keep the newest <see cref="KeepBackups"/> backups of one file (the time stamp in the name sorts by age).</summary>
    private static void Prune(string directory, string name)
    {
      var old = Directory.GetFiles(directory, name + ".*.bak").OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).Skip(KeepBackups);
      foreach (var path in old)
      {
        try
        {
          File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      }
    }
  }
}
