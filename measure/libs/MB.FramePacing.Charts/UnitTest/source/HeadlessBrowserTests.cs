//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The headless browser that saves the PNGs: when it is run again (once more after a failure, and without the sandbox where Linux forbids
//* it), that its error text is read while a helper keeps the output open (a script stands in for the browser), and that an image from an
//* earlier run is never taken for the new one (with a real browser; skipped without one).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class HeadlessBrowserTests
  {
    private const string SandboxError = "FATAL:zygote_host_impl_linux.cc: No usable sandbox!";

    private static readonly BrowserAttempt g_saved = new(true, 0, string.Empty);
    private static readonly BrowserAttempt g_aborted = new(false, 134, string.Empty);
    private static readonly BrowserAttempt g_timedOut = new(false, null, string.Empty);
    private static readonly BrowserAttempt g_noSandbox = new(false, 134, SandboxError);

    /// <summary>Runs the given results in order and notes each run's "without the sandbox".</summary>
    private static (BrowserAttempt Result, List<bool> Runs) Save(bool linux, params BrowserAttempt[] results)
    {
      var runs = new List<bool>();
      var result = HeadlessBrowser.SaveWithRetries(
        noSandbox =>
        {
          runs.Add(noSandbox);
          return results[runs.Count - 1];
        },
        linux
      );
      return (result, runs);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ABrowserThatSaves_RunsOnce(bool linux)
    {
      var (result, runs) = Save(linux, g_saved);
      Assert.That(result, Is.EqualTo(g_saved));
      Assert.That(runs, Is.EqualTo(new[] { false }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AFailure_GetsOneMoreRun(bool linux)
    {
      var (result, runs) = Save(linux, g_aborted, g_saved);
      Assert.That(result.Saved, Is.True);
      Assert.That(runs, Is.EqualTo(new[] { false, false }), "the same settings again");

      (result, runs) = Save(linux, g_timedOut, g_saved);
      Assert.That(result.Saved, Is.True, "a browser that hung");
      Assert.That(runs, Is.EqualTo(new[] { false, false }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASecondFailure_IsTheResult(bool linux)
    {
      var (result, runs) = Save(linux, g_aborted, g_timedOut);
      Assert.That(result, Is.EqualTo(g_timedOut), "the last run's failure");
      Assert.That(result.TimedOut, Is.True);
      Assert.That(runs, Has.Count.EqualTo(2));

      (result, _) = Save(linux, g_timedOut, g_aborted);
      Assert.That(result.TimedOut, Is.False, "it exited");
      Assert.That(result.ExitCode, Is.EqualTo(134));
    }

    [Test]
    public void Linux_ASandboxFailure_RunsWithoutTheSandbox()
    {
      var (result, runs) = Save(true, g_noSandbox, g_saved);
      Assert.That(result.Saved, Is.True);
      Assert.That(runs, Is.EqualTo(new[] { false, true }));
    }

    /// <summary>The run without the sandbox does not use up the one more run a failure gets.</summary>
    [Test]
    public void Linux_WithoutTheSandbox_AFailureStillGetsOneMoreRun()
    {
      var (result, runs) = Save(true, g_noSandbox, g_aborted, g_saved);
      Assert.That(result.Saved, Is.True);
      Assert.That(runs, Is.EqualTo(new[] { false, true, true }), "the sandbox stays off");

      (result, runs) = Save(true, g_noSandbox, g_noSandbox, g_noSandbox);
      Assert.That(result, Is.EqualTo(g_noSandbox));
      Assert.That(runs, Is.EqualTo(new[] { false, true, true }), "and no fourth run");

      (result, runs) = Save(true, g_aborted, g_noSandbox, g_saved);
      Assert.That(result.Saved, Is.True, "the sandbox failure on the second run");
      Assert.That(runs, Is.EqualTo(new[] { false, false, true }));
    }

    [Test]
    public void ElsewhereTheSandboxStaysOn()
    {
      var (result, runs) = Save(false, g_noSandbox, g_noSandbox);
      Assert.That(result.Saved, Is.False);
      Assert.That(runs, Is.EqualTo(new[] { false, false }));
    }

    /// <summary>
    /// A browser that says why it fails and exits, while a helper it started keeps its error output open (as a real browser's helpers do):
    /// the text is still read. It is what the next run is decided by, and what the error shows. The stand-in browser is a script.
    /// </summary>
    [Test]
    public void SavePng_ReadsTheErrorsOfABrowserWhoseHelperKeepsTheOutputOpen()
    {
      string directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        string svg = Path.Combine(directory, "card.svg");
        File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"40\"/>", new UTF8Encoding(false));
        string browser;
        if (OperatingSystem.IsWindows())
        {
          browser = Path.Combine(directory, "browser.cmd");
          File.WriteAllText(browser, "@echo off\r\necho No usable sandbox 1>&2\r\nstart \"\" /b ping -n 8 127.0.0.1 >nul\r\nexit /b 134\r\n");
        }
        else
        {
          browser = Path.Combine(directory, "browser.sh");
          File.WriteAllText(browser, "#!/bin/sh\necho 'No usable sandbox' >&2\nsleep 8 &\nexit 134\n");
          File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var error = Assert.Throws<InvalidOperationException>(() => HeadlessBrowser.SavePng(svg, Path.Combine(directory, "card.png"), browser));
        Assert.That(error!.Message, Does.Contain("exit code 134"));
        Assert.That(error.Message, Does.Contain("No usable sandbox"));
      }
      finally
      {
        Directory.Delete(directory, recursive: true);
      }
    }

    /// <summary>
    /// A whole PNG already at the target (rendering to the same path twice) is replaced: the browser is ended as soon as the file is whole,
    /// so the old image would be taken for the new one.
    /// </summary>
    [Test]
    public void SavePng_ReplacesAnImageThatIsAlreadyThere()
    {
      if (HeadlessBrowser.Find() is not { } browser)
      {
        Assert.Ignore("No Edge or Chrome on this machine");
        return;
      }
      string directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        string svg = Path.Combine(directory, "card.svg");
        string png = Path.Combine(directory, "card.png");
        File.WriteAllText(
          svg,
          "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"40\"><rect width=\"120\" height=\"40\" fill=\"#204060\"/></svg>",
          new UTF8Encoding(false)
        );
        // Not an image, but whole as the browser's check sees it: it ends with a PNG's last chunk
        var old = new byte[64];
        Encoding.ASCII.GetBytes("IEND").CopyTo(old, old.Length - 8);
        File.WriteAllBytes(png, old);

        HeadlessBrowser.SavePng(svg, png, browser);

        byte[] saved = File.ReadAllBytes(png);
        Assert.That(saved.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }), "a PNG");
        int BigEndian(int offset) => (saved[offset] << 24) | (saved[offset + 1] << 16) | (saved[offset + 2] << 8) | saved[offset + 3];
        Assert.That((BigEndian(16), BigEndian(20)), Is.EqualTo((240, 80)), "the SVG at twice its size");
      }
      finally
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }
}
