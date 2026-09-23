//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Avalonia application.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MB.FramePacing.Gui.ViewModels;
using MB.FramePacing.Gui.Views;

namespace MB.FramePacing.Gui
{
  public partial class App : Application
  {
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
        var window = new MainWindow();
        var viewModel = new MainWindowViewModel(new DialogService(window));
        window.DataContext = viewModel;
        desktop.MainWindow = window;
        // Find ffmpeg once the window is visible, and open the setup dialog on top of it if it is missing
        window.Opened += async (_, _) => await viewModel.InitializeAsync();
      }
      base.OnFrameworkInitializationCompleted();
    }
  }
}
