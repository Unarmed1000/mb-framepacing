//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saves an SVG as a PNG at twice its size through a headless Edge or Chrome, as mb-framepacing-explained's diagrams do (find_browser and
//* save_png in generate_diagrams.py): the browser draws the style sheet and the fonts exactly as it shows the SVG. The browser comes from
//* MB_BROWSER, the usual install locations, then PATH.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MB.FramePacing.Charts
{
  public static class HeadlessBrowser
  {
    public const string EnvironmentVariable = "MB_BROWSER";

    private static readonly TimeSpan g_timeout = TimeSpan.FromSeconds(60);

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
      string found =
        browser ?? Find() ?? throw new InvalidOperationException($"Saving a PNG needs Edge or Chrome: set {EnvironmentVariable} to its executable");
      string content = File.ReadAllText(svgPath);
      int width = int.Parse(Regex.Match(content, "width=\"(\\d+)\"").Groups[1].Value);
      int height = int.Parse(Regex.Match(content, "height=\"(\\d+)\"").Groups[1].Value);

      var result = SaveWithRetries(noSandbox => Attempt(found, svgPath, pngPath, width, height, noSandbox), OperatingSystem.IsLinux());
      if (result.Saved)
        return;
      if (result.TimedOut)
        throw new TimeoutException($"{found} did not save {pngPath} within {g_timeout.TotalSeconds:0} s{Tail(result.Errors)}");
      throw new InvalidOperationException($"{found} failed to save {pngPath} (exit code {result.ExitCode}){Tail(result.Errors)}");
    }

    /// <summary>
    /// Runs the browser (<paramref name="attempt"/>; its argument: without the sandbox) until it has saved the PNG, and gives the last run.
    /// <list type="bullet">
    /// <item>
    /// Linux without unprivileged user namespaces (Ubuntu 24.04's AppArmor rule, containers, CI machines): the browser falls back to its
    /// setuid sandbox helper and aborts when that is not installed as root. It opens only this local SVG and loads nothing else, so it
    /// runs without the sandbox from then on.
    /// </item>
    /// <item>
    /// A browser that fails otherwise gets one more run: on a busy machine a headless browser now and then aborts or hangs while it starts
    /// (CI machines did both in one run, and saved the same image on the next).
    /// </item>
    /// </list>
    /// </summary>
    internal static BrowserAttempt SaveWithRetries(Func<bool, BrowserAttempt> attempt, bool linux)
    {
      bool noSandbox = false;
      bool repeated = false;
      while (true)
      {
        var result = attempt(noSandbox);
        if (result.Saved)
          return result;
        if (linux && !noSandbox && result.Errors.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
          noSandbox = true;
          continue;
        }
        if (repeated)
          return result;
        repeated = true;
      }
    }

    /// <summary>One run of the browser, in a profile of its own that is deleted afterwards.</summary>
    private static BrowserAttempt Attempt(string browser, string svgPath, string pngPath, int width, int height, bool noSandbox)
    {
      // An image that is already there would count as this run's: the browser is ended as soon as the file is whole
      File.Delete(pngPath);
      string profile = Path.Combine(Path.GetTempPath(), "mb-framepacing-browser-" + Guid.NewGuid().ToString("N"));
      try
      {
        var arguments = new List<string>();
        if (noSandbox)
          arguments.Add("--no-sandbox");
        arguments.AddRange(
          new[]
          {
            "--default-background-color=00000000",
            "--headless=new",
            "--disable-gpu",
            "--hide-scrollbars",
            "--force-device-scale-factor=2",
            // A fresh profile every time: no first run dialogs, no default browser question, no extensions
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            $"--user-data-dir={profile}",
            $"--window-size={width},{height}",
            $"--screenshot={Path.GetFullPath(pngPath)}",
          }
        );
        // macOS: a new profile would ask the keychain for its encryption key, which blocks a headless browser
        if (OperatingSystem.IsMacOS())
          arguments.Add("--use-mock-keychain");
        // Linux: containers and CI machines often have a small /dev/shm
        if (OperatingSystem.IsLinux())
          arguments.Add("--disable-dev-shm-usage");
        arguments.Add(new Uri(Path.GetFullPath(svgPath)).AbsoluteUri);
        return Run(browser, arguments, pngPath);
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

    /// <summary>
    /// Run the browser once. A browser that saved the whole PNG but does not exit (headless Chrome on macOS can hang while shutting down) is
    /// ended and counts as done. Only the browser's own process is ended: its helpers end with it, and ending the whole process tree
    /// (Process.Kill(entireProcessTree)) took down the GitHub macOS runner that ran the tests.
    /// </summary>
    private static BrowserAttempt Run(string browser, IReadOnlyList<string> arguments, string pngPath)
    {
      var start = new ProcessStartInfo(browser)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in arguments)
        start.ArgumentList.Add(argument);
      using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {browser}");
      _ = process.StandardOutput.ReadToEndAsync();
      var errors = new StringBuilder();
      var reading = ReadInto(process.StandardError, errors);
      var elapsed = Stopwatch.StartNew();
      while (!process.WaitForExit(250))
      {
        if (IsCompletePng(pngPath))
        {
          process.Kill();
          // With a time limit: without one, WaitForExit also waits for the redirected output to close, which a helper process the browser
          // started on its own can keep open
          process.WaitForExit(5000);
          return new BrowserAttempt(true, 0, Text(reading, errors));
        }
        if (elapsed.Elapsed > g_timeout)
        {
          process.Kill();
          return new BrowserAttempt(false, null, Text(reading, errors));
        }
      }
      int exitCode = process.ExitCode;
      return new BrowserAttempt(exitCode == 0 && IsCompletePng(pngPath), exitCode, Text(reading, errors));
    }

    /// <summary>
    /// Collects the browser's error output as it arrives. Reading it to its end instead would lose all of it whenever a helper process the
    /// browser started keeps the output open after the browser is gone: the text that says why it failed, which decides the next run.
    /// </summary>
    private static async Task ReadInto(StreamReader reader, StringBuilder text)
    {
      var buffer = new char[1024];
      try
      {
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
          lock (text)
            text.Append(buffer, 0, count);
        }
      }
      // The process was disposed while its output was still open
      catch (IOException) { }
      catch (ObjectDisposedException) { }
    }

    /// <summary>The PNG is there and whole: it ends with its IEND chunk, which the browser writes last.</summary>
    private static bool IsCompletePng(string path)
    {
      try
      {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < 20)
          return false;
        var end = new byte[12];
        stream.Seek(-end.Length, SeekOrigin.End);
        stream.ReadExactly(end);
        return end[4] == 'I' && end[5] == 'E' && end[6] == 'N' && end[7] == 'D';
      }
      catch (IOException)
      {
        return false;
      }
      catch (UnauthorizedAccessException)
      {
        return false;
      }
    }

    /// <summary>What the browser has written to its error output: all of it when the output closes in time, else what has arrived.</summary>
    private static string Text(Task reading, StringBuilder errors)
    {
      reading.Wait(2000);
      lock (errors)
        return errors.ToString();
    }

    /// <summary>The browser's last error lines, for the exception: they say why it failed.</summary>
    private static string Tail(string errors)
    {
      var lines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      return lines.Length == 0 ? string.Empty : ":" + Environment.NewLine + string.Join(Environment.NewLine, lines.TakeLast(20));
    }
  }
}
