//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The GUI's log: NLog into a file per day (gui-yyyy-MM-dd.log) in the logs folder next to the GUI settings, the last KeepDays days kept. An
//* --output-root run (also DocImages) logs into its output folder instead, never the user's. Exceptions nothing else handled are logged
//* here too, with their stack, before the application ends as it would without the log.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace MB.FramePacing.Gui
{
  public static class GuiLogging
  {
    public const string FilePrefix = "gui-";
    public const string DateFormat = "yyyy-MM-dd";
    public const int KeepDays = 7;

    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();
    private static bool g_handlersAdded;

    /// <summary>The folder the log files are in (set by <see cref="Configure"/>).</summary>
    public static string Directory { get; private set; } = DefaultDirectory();

    /// <summary>
    /// Log into <see cref="Directory"/> from now on: the logs folder next to the GUI settings, or the output root's for an --output-root run.
    /// Deletes the files older than <see cref="KeepDays"/> days, and logs the exceptions nothing else handles.
    /// </summary>
    public static void Configure()
    {
      Directory = DefaultDirectory();
      DeleteOld(Directory, DateTime.Today, KeepDays);
      var file = new FileTarget("file")
      {
        // The folder is a literal: its path may hold characters a layout would read
        FileName = new SimpleLayout("${var:logDirectory}" + Path.DirectorySeparatorChar + FilePrefix + "${date:format=" + DateFormat + "}.log"),
        Layout = "${longdate} ${level:uppercase=true} ${logger:shortName=true}: ${message}${onexception:${newline}${exception:format=tostring}}",
        Encoding = System.Text.Encoding.UTF8,
      };
      var config = new LoggingConfiguration();
      config.Variables["logDirectory"] = Directory;
      config.AddRule(LogLevel.Info, LogLevel.Fatal, file);
      LogManager.Configuration = config;

      if (!g_handlersAdded)
      {
        g_handlersAdded = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
          g_logger.Fatal(e.ExceptionObject as Exception, "Unhandled exception: {0}", e.ExceptionObject);
          LogManager.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => g_logger.Error(e.Exception, "Unobserved task exception");
      }
    }

    /// <summary>Delete the log files in <paramref name="directory"/> dated more than <paramref name="keepDays"/> days before <paramref name="today"/>.</summary>
    public static void DeleteOld(string directory, DateTime today, int keepDays)
    {
      if (!System.IO.Directory.Exists(directory))
        return;
      foreach (var path in System.IO.Directory.EnumerateFiles(directory, FilePrefix + "*.log"))
      {
        string date = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
        if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
          continue;
        if (day > today.AddDays(-keepDays))
          continue;
        try
        {
          File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      }
    }

    private static string DefaultDirectory() =>
      Path.Combine(Program.OutputRoot ?? Path.GetDirectoryName(GuiSettings.FilePath) ?? Path.GetTempPath(), "logs");
  }
}
