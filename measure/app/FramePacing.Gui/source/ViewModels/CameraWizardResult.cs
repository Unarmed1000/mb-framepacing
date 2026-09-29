//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the camera rig wizard set up: the saved camera to capture with and the source it films.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  /// <summary>What the camera rig wizard set up: the saved camera to capture with and the source it films.</summary>
  public sealed record CameraWizardResult(string RigName, CameraSourceChoice Source);
}
