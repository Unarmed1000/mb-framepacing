//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Calibrated cameras saved by name (EXPERIMENTAL camera support), so a mounted camera is calibrated once and later captures only verify it.
//* The library lives next to the configuration file (camera-rigs/<name>.camera-rig.json), so the command line and the GUI share it.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture.Camera
{
  public static class CameraRigLibrary
  {
    public const string DirectoryName = "camera-rigs";

    /// <summary>camera-rigs next to the configuration file in use (the portable one when present).</summary>
    public static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(FramePacingConfig.ResolvePath())!, DirectoryName);

    /// <summary>Letters, digits, '-', '_', '.' and spaces, at most 64 characters, not starting with '.' or a space.</summary>
    public static bool IsValidName(string? name) =>
      !string.IsNullOrWhiteSpace(name)
      && name.Length <= 64
      && name[0] != '.'
      && name[0] != ' '
      && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ');

    public static string PathFor(string name, string? directory = null)
    {
      if (!IsValidName(name))
        throw new ArgumentException($"'{name}' is not a valid camera name: use letters, digits, '-', '_', '.' and spaces", nameof(name));
      return Path.Combine(directory ?? DefaultDirectory, name.Trim() + CameraRig.FileExtension);
    }

    /// <summary>Every saved camera, by name. Files that can not be read are listed with their error.</summary>
    public static IReadOnlyList<SavedCameraRig> List(string? directory = null)
    {
      directory ??= DefaultDirectory;
      var list = new List<SavedCameraRig>();
      if (!Directory.Exists(directory))
        return list;
      foreach (var path in Directory.GetFiles(directory, "*" + CameraRig.FileExtension))
      {
        var name = Path.GetFileName(path)[..^CameraRig.FileExtension.Length];
        try
        {
          list.Add(new SavedCameraRig(name, path, CameraRig.Load(path)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
          list.Add(new SavedCameraRig(name, path, null, ex.Message));
        }
      }
      return list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Save <paramref name="rig"/> as <paramref name="name"/> (replacing a camera of that name; the replaced version is kept in the backup folder).
    /// Returns the file path.
    /// </summary>
    public static string Save(CameraRig rig, string name, string? directory = null)
    {
      ArgumentNullException.ThrowIfNull(rig);
      var path = PathFor(name, directory);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      (rig with { Name = name.Trim() }).Save(path);
      return path;
    }

    public static bool Exists(string name, string? directory = null) => IsValidName(name) && File.Exists(PathFor(name, directory));

    /// <summary>Delete a saved camera by moving its file to the backup folder (<see cref="SettingsFile"/>). Returns the backup path.</summary>
    public static string Delete(string name, string? directory = null)
    {
      var path = PathFor(name, directory);
      if (!File.Exists(path))
        throw new FileNotFoundException($"There is no saved camera '{name}'", path);
      return SettingsFile.Delete(path);
    }

    /// <summary>A rig file path, or the name of a saved camera. Throws with the saved names when neither exists.</summary>
    public static string Resolve(string nameOrPath, string? directory = null)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(nameOrPath);
      if (File.Exists(nameOrPath))
        return Path.GetFullPath(nameOrPath);
      if (IsValidName(nameOrPath) && File.Exists(PathFor(nameOrPath, directory)))
        return PathFor(nameOrPath, directory);
      var names = List(directory).Select(r => r.Name).ToList();
      throw new FileNotFoundException(
        $"'{nameOrPath}' is neither a camera rig file nor a saved camera. "
          + (names.Count > 0 ? $"Saved cameras: {string.Join(", ", names)}." : "No cameras are saved yet: 'camera-rig calibrate --name <name>'.")
      );
    }

    public static CameraRig Load(string nameOrPath, string? directory = null) => CameraRig.Load(Resolve(nameOrPath, directory));
  }
}
