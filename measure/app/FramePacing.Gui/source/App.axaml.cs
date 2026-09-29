//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Avalonia application.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MB.FramePacing.Gui.ViewModels;
using MB.FramePacing.Gui.Views;
using NLog;

namespace MB.FramePacing.Gui
{
  public partial class App : Application
  {
    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
      // Logged before the application ends, as it would without the log
      Dispatcher.UIThread.UnhandledException += (_, e) =>
      {
        g_logger.Fatal(e.Exception, "Unhandled exception on the UI thread");
        LogManager.Flush();
      };
      g_logger.Info("mb-framepacing-gui {0} started; logging to {1}", MainWindowViewModel.Version, GuiLogging.Directory);
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
        var window = new MainWindow();
        var viewModel = new MainWindowViewModel(new DialogService(window));
        window.DataContext = viewModel;
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => LogManager.Shutdown();
        // Find ffmpeg once the window is visible, and open the setup dialog on top of it if it is missing
        window.Opened += async (_, _) =>
        {
          try
          {
            await viewModel.InitializeAsync();
          }
          catch (Exception ex)
          {
            g_logger.Error(ex, "Starting up failed");
          }
        };
      }
      base.OnFrameworkInitializationCompleted();
    }
  }
}
