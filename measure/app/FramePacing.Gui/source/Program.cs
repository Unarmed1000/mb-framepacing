//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* mb-framepacing-gui entry point.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using Avalonia;

namespace MB.FramePacing.Gui
{
  internal static class Program
  {
    /// <summary>Driven by a tool (tools/DocImages): no user setting is loaded or saved, captures go to <see cref="OutputRoot"/>.</summary>
    public static bool Automation { get; private set; }

    /// <summary>'--output-root &lt;dir&gt;': store captures there instead of the remembered folder.</summary>
    public static string? OutputRoot { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
      int outputIndex = Array.IndexOf(args, "--output-root");
      if (outputIndex >= 0 && outputIndex + 1 < args.Length)
        OutputRoot = System.IO.Path.GetFullPath(args[outputIndex + 1]);
      GuiLogging.Configure();
      BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Used by tools that drive the GUI (tools/DocImages): like --output-root, and never touch the user's settings.</summary>
    internal static void ConfigureForAutomation(string outputRoot)
    {
      Automation = true;
      OutputRoot = outputRoot;
      GuiLogging.Configure();
    }

    // Also used by the Avalonia designer
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
  }
}
