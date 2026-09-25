//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* mb-framepacing command line: devices, capture, analyze, selftest.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.Linq;
using System.Reflection;
using MB.FramePacing.App.Commands;
using NLog;
using NLog.Targets;
using Spectre.Console;

namespace MB.FramePacing.App
{
  internal static class Program
  {
    public const int ResultSuccess = 0;
    public const int ResultError = 1;

    public static readonly string VersionString = ReadVersion();

    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private static int Main(string[] args)
    {
      // -v .. -vvvv selects the log level; it is handled here and stripped before parsing
      int verbosity = args.Where(IsVerbosityToken).Select(a => a.Length - 1).DefaultIfEmpty(0).Max();
      ConfigureLogging(verbosity);

      var root = new RootCommand(
        $"mb-framepacing {VersionString} - capture a game's display output and measure animation error from in-frame markers"
      )
      {
        DevicesCommand.Create(),
        CaptureCommand.Create(),
        LocateCommand.Create(),
        CameraRigCommand.Create(),
        ImportCommand.Create(),
        AnalyzeCommand.Create(),
        MarkerSizeCommand.Create(),
        SelfTestCommand.Create(),
        ConfigCommand.Create(),
      };
      root.Options.Add(CommonOptions.Config);
      root.Options.Add(new Option<bool>("-v") { Description = "Verbose logging, repeat for more detail (-vv, -vvv, -vvvv)." });

      try
      {
        return root.Parse(args.Where(a => !IsVerbosityToken(a)).ToArray()).Invoke();
      }
      catch (Exception ex)
      {
        ReportError(ex);
        return ResultError;
      }
    }

    public static void ReportError(Exception ex)
    {
      g_logger.Error(ex, "ERROR: {0}", ex.Message);
      var inner = ex is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : new[] { ex };
      foreach (var error in inner)
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] {error.Message}");
    }

    private static bool IsVerbosityToken(string arg) => arg.Length >= 2 && arg[0] == '-' && arg.Skip(1).All(c => c == 'v');

    private static void ConfigureLogging(int verbosity)
    {
      if (verbosity <= 0)
        return;
      var level = verbosity switch
      {
        1 => LogLevel.Info,
        2 => LogLevel.Debug,
        _ => LogLevel.Trace,
      };
      var target = new ConsoleTarget("console")
      {
        Layout = "${time} ${level:uppercase=true} ${logger:shortName=true}: ${message}${onexception:${newline}${exception}}",
      };
      LogManager.Setup().LoadConfiguration(c => c.ForLogger(level).WriteTo(target));
    }

    private static string ReadVersion()
    {
      var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
      int plus = version.IndexOf('+');
      return plus >= 0 ? version.Substring(0, plus) : version;
    }
  }
}
