//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Main window.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
      (DataContext as MainWindowViewModel)?.SaveSettings();
      base.OnClosing(e);
    }
  }
}
