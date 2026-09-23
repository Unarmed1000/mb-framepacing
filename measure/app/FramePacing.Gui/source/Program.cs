//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* mb-framepacing-gui entry point.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using Avalonia;

namespace MB.FramePacing.Gui
{
  internal static class Program
  {
    /// <summary>'--demo': start a capture of the built in synthetic test game right away (try the tool without hardware).</summary>
    public static bool Demo { get; private set; }

    /// <summary>'--output-root &lt;dir&gt;': store captures there instead of the remembered folder.</summary>
    public static string? OutputRoot { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
      Demo = Array.Exists(args, arg => arg == "--demo");
      int outputIndex = Array.IndexOf(args, "--output-root");
      if (outputIndex >= 0 && outputIndex + 1 < args.Length)
        OutputRoot = System.IO.Path.GetFullPath(args[outputIndex + 1]);
      BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Used by tools that drive the GUI (tools/DocImages): behave like --demo --output-root, never touch the user's settings.</summary>
    internal static void ConfigureForAutomation(string outputRoot)
    {
      Demo = true;
      OutputRoot = outputRoot;
    }

    // Also used by the Avalonia designer
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
  }
}
