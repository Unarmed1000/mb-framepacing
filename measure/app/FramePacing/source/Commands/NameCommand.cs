//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'name': set or clear the name stored in a capture's capture.json, which reports show instead of the runs' sequence id.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using MB.FramePacing.Capture;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class NameCommand
  {
    public static Command Create()
    {
      var folderArgument = new Argument<string>("capture") { Description = "The capture folder (the one holding capture.json)." };
      var nameArgument = new Argument<string?>("name")
      {
        Description = "The name the reports show instead of the sequence id; leave it out to clear it.",
        Arity = ArgumentArity.ZeroOrOne,
      };
      var command = new Command("name", "Set or clear the name stored in a capture (capture.json); analyse again to use it in the reports.")
      {
        folderArgument,
        nameArgument,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          string folder = Path.GetFullPath(parseResult.GetValue(folderArgument)!);
          var session =
            CaptureSessionInfo.TryLoad(folder) ?? throw new FileNotFoundException($"'{folder}' has no {CaptureSessionInfo.FileName}", folder);
          string? name = parseResult.GetValue(nameArgument) is { Length: > 0 } text ? text : null;
          (session with { Name = name }).Save(folder);
          AnsiConsole.MarkupLineInterpolated(
            $"{CaptureSessionInfo.FileName}: {(name != null ? $"name '{name}'" : "no name")}. Analyse again ('analyze') to use it in the reports."
          );
          return Program.ResultSuccess;
        }
        catch (Exception ex)
        {
          Program.ReportError(ex);
          return Program.ResultError;
        }
      });
      return command;
    }
  }
}
