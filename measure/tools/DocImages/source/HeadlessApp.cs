//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The Avalonia app builder DocImages uses: the real GUI on the headless platform with the Skia renderer.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia;
using Avalonia.Headless;
using MB.FramePacing.Gui;

namespace MB.FramePacing.DocImages
{
  public static class HeadlessApp
  {
    // Used by HeadlessUnitTestSession: the real application with the Skia renderer, so text and charts render exactly like on screen
    public static AppBuilder BuildAvaloniaApp() =>
      AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont();
  }
}
