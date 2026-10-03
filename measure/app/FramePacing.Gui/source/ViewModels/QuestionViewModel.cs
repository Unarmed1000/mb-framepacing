//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A yes/no question in a dialog (QuestionWindow), with a "Remember my choice" check box: the playback page's questions about the recording.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class QuestionViewModel : ObservableObject
  {
    public QuestionViewModel(string title, string text, string yes, string no, string rememberText)
    {
      Title = title;
      Text = text;
      Yes = yes;
      No = no;
      RememberText = rememberText;
    }

    public string Title { get; }

    public string Text { get; }

    /// <summary>The answer that writes a video.</summary>
    public string Yes { get; }

    /// <summary>The answer that does not.</summary>
    public string No { get; }

    /// <summary>What remembering does ("Remember my choice: stored as playbackTranscode in the configuration").</summary>
    public string RememberText { get; }

    [ObservableProperty]
    public partial bool Remember { get; set; }

    /// <summary>The dialog closes with the answer.</summary>
    public event Action<QuestionAnswer>? CloseRequested;

    [RelayCommand]
    private void AnswerYes() => CloseRequested?.Invoke(new QuestionAnswer(true, Remember));

    [RelayCommand]
    private void AnswerNo() => CloseRequested?.Invoke(new QuestionAnswer(false, Remember));
  }
}
