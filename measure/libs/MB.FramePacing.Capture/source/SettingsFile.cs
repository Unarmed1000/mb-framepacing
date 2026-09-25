//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saves and deletes settings files (the configuration file, the GUI settings, saved cameras) safely, keeping the old version. The "backup" folder
//* next to a file holds:
//* - <file name>.bak: the version the last save replaced, or the deleted file (hand edits included: it is whatever was on the disk);
//* - <file name>.v<N>.bak: the last version in format N, written when a save changes the file's format version (formatVersion).
//* Every file is written to a uniquely named temporary file, flushed to the disk and renamed into place, so each file is always either its old or
//* its new version, even after a crash or a power cut, and two programs saving at once do not collide. The backups are written before the file
//* is replaced; if they can not be written, the save fails and the file is left alone. Restore a backup by copying it back.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MB.FramePacing.Capture
{
  public static class SettingsFile
  {
    public const string BackupDirectoryName = "backup";

    /// <summary>The backup folder for <paramref name="path"/>.</summary>
    public static string BackupDirectory(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, BackupDirectoryName);

    /// <summary>The backup of the version the last save replaced (or of the deleted file).</summary>
    public static string PreviousPath(string path) => Path.Combine(BackupDirectory(path), Path.GetFileName(path) + ".bak");

    /// <summary>The backup of the last version in format <paramref name="formatVersion"/>, kept when a save changed the format.</summary>
    public static string VersionPath(string path, int formatVersion) =>
      Path.Combine(BackupDirectory(path), string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}.v{formatVersion}.bak"));

    /// <summary>
    /// Write <paramref name="contents"/>, a settings file in format <paramref name="formatVersion"/>, to <paramref name="path"/> (creating the
    /// folder). When the file exists with other contents, it is backed up first (<see cref="PreviousPath"/>), and when its format differs, also as
    /// the last version in its format (<see cref="VersionPath"/>). Returns the previous version's backup, or null when there was nothing to back up
    /// (no file yet, or the same contents).
    /// </summary>
    public static string? Write(string path, string contents, int formatVersion)
    {
      path = Path.GetFullPath(path);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
      string? backup = null;
      if (File.Exists(path))
      {
        var current = File.ReadAllBytes(path);
        if (current.AsSpan().SequenceEqual(bytes))
          return null;
        // The old version is safe on the disk before anything is replaced; if this throws, the save stops here
        Directory.CreateDirectory(BackupDirectory(path));
        if (ReadFormatVersion(current) is { } currentVersion && currentVersion != formatVersion)
          ReplaceDurable(VersionPath(path, currentVersion), current);
        backup = PreviousPath(path);
        ReplaceDurable(backup, current);
      }
      ReplaceDurable(path, bytes);
      return backup;
    }

    /// <summary>" The previous version is kept in ...", for errors about a file that can not be read; empty when there is no backup.</summary>
    public static string BackupHint(string path) =>
      File.Exists(PreviousPath(path)) ? $" The previous version is kept in '{PreviousPath(path)}'; copy it back to restore it." : string.Empty;

    /// <summary>Delete <paramref name="path"/>, keeping it as <see cref="PreviousPath"/>. Returns the backup path.</summary>
    public static string Delete(string path)
    {
      path = Path.GetFullPath(path);
      if (!File.Exists(path))
        throw new FileNotFoundException($"'{path}' does not exist", path);
      // The backup is on the disk before the file goes
      Directory.CreateDirectory(BackupDirectory(path));
      var backup = PreviousPath(path);
      ReplaceDurable(backup, File.ReadAllBytes(path));
      File.Delete(path);
      return backup;
    }

    /// <summary>
    /// The formatVersion of a settings file (any letter case, comments allowed; a file without it is format 1), or null when it is not a JSON
    /// object.
    /// </summary>
    public static int? ReadFormatVersion(byte[] json)
    {
      try
      {
        using var document = JsonDocument.Parse(
          json,
          new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
        );
        if (document.RootElement.ValueKind != JsonValueKind.Object)
          return null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
          if (string.Equals(property.Name, "formatVersion", StringComparison.OrdinalIgnoreCase))
            return property.Value.TryGetInt32(out int version) ? version : null;
        }
        return 1;
      }
      catch (JsonException)
      {
        return null;
      }
    }

    /// <summary>
    /// Replace (or create) <paramref name="path"/> with <paramref name="contents"/>: a uniquely named temporary file in the same folder (so the
    /// rename stays on one volume and is atomic, and two programs do not collide), flushed to the disk, then renamed over the target.
    /// </summary>
    private static void ReplaceDurable(string path, byte[] contents)
    {
      var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
      try
      {
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
          stream.Write(contents, 0, contents.Length);
          // Onto the disk, not just the OS cache: otherwise a power cut can leave the renamed file empty
          stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
      }
      catch
      {
        try
        {
          File.Delete(temp);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        throw;
      }
    }
  }
}
