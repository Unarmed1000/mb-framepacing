//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saves an SVG as a PNG at twice its size through a headless Edge or Chrome, as mb-framepacing-explained's diagrams do (find_browser and
//* save_png in generate_diagrams.py): the browser draws the style sheet and the fonts exactly as it shows the SVG. The browser comes from
//* MB_BROWSER, the usual install locations, then PATH.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MB.FramePacing.Charts
{
  public static class HeadlessBrowser
  {
    public const string EnvironmentVariable = "MB_BROWSER";

    private static readonly string[] g_candidates =
    {
      @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
      @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
      @"C:\Program Files\Google\Chrome\Application\chrome.exe",
      "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
      "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    };

    private static readonly string[] g_names = { "msedge", "microsoft-edge", "google-chrome", "chrome", "chromium", "chromium-browser" };

    /// <summary>A Chromium browser: <see cref="EnvironmentVariable"/>, the usual install locations, then PATH. Null when there is none.</summary>
    public static string? Find()
    {
      string? configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
      if (!string.IsNullOrEmpty(configured))
        return File.Exists(configured)
          ? configured
          : throw new FileNotFoundException($"{EnvironmentVariable} does not point to a file: {configured}");
      foreach (var candidate in g_candidates)
      {
        if (File.Exists(candidate))
          return candidate;
      }
      var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
      string[] extensions = OperatingSystem.IsWindows() ? new[] { ".exe", string.Empty } : new[] { string.Empty };
      return g_names
        .SelectMany(name => path.SelectMany(directory => extensions.Select(extension => Path.Combine(directory, name + extension))))
        .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Screenshot the SVG at <paramref name="svgPath"/> into <paramref name="pngPath"/> at twice its size, with a transparent background unless
    /// the SVG draws one. Throws when there is no browser or it fails.
    /// </summary>
    public static void SavePng(string svgPath, string pngPath, string? browser = null)
    {
      browser ??= Find() ?? throw new InvalidOperationException($"Saving a PNG needs Edge or Chrome: set {EnvironmentVariable} to its executable");
      string content = File.ReadAllText(svgPath);
      int width = int.Parse(Regex.Match(content, "width=\"(\\d+)\"").Groups[1].Value);
      int height = int.Parse(Regex.Match(content, "height=\"(\\d+)\"").Groups[1].Value);
      string profile = Path.Combine(Path.GetTempPath(), "mb-framepacing-browser-" + Guid.NewGuid().ToString("N"));
      try
      {
        var start = new ProcessStartInfo(browser)
        {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
        };
        foreach (
          var argument in new[]
          {
            "--default-background-color=00000000",
            "--headless=new",
            "--disable-gpu",
            "--hide-scrollbars",
            "--force-device-scale-factor=2",
            $"--user-data-dir={profile}",
            $"--window-size={width},{height}",
            $"--screenshot={Path.GetFullPath(pngPath)}",
            new Uri(Path.GetFullPath(svgPath)).AbsoluteUri,
          }
        )
          start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {browser}");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
          process.Kill(entireProcessTree: true);
          throw new TimeoutException($"{browser} did not save {pngPath} within 60 s");
        }
        if (process.ExitCode != 0 || !File.Exists(pngPath))
          throw new InvalidOperationException($"{browser} failed to save {pngPath} (exit code {process.ExitCode})");
      }
      finally
      {
        try
        {
          if (System.IO.Directory.Exists(profile))
            System.IO.Directory.Delete(profile, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      }
    }
  }
}
