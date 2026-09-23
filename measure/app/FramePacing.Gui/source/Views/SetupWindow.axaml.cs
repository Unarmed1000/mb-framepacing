//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Setup dialog window. Returns true from ShowDialog when the settings were saved.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class SetupWindow : Window
  {
    public SetupWindow()
    {
      InitializeComponent();
      DataContextChanged += (_, _) =>
      {
        if (DataContext is SetupViewModel viewModel)
          viewModel.CloseRequested += saved => Close(saved);
      };
    }
  }
}
