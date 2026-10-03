//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The URL a playback page uses for its video: relative to the page's folder, so the folder and the recording can move together, and a
//* file:/// URL when there is no relative way (another drive). Each path segment is percent-encoded (spaces, '#', '%', ...).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackUrl
  {
    /// <summary>The URL of <paramref name="file"/> for a page in <paramref name="pageDirectory"/>.</summary>
    public static string For(string pageDirectory, string file)
    {
      string from = Path.GetFullPath(pageDirectory);
      string to = Path.GetFullPath(file);
      string relative = Path.GetRelativePath(from, to);
      if (Path.IsPathRooted(relative))
        return new Uri(to).AbsoluteUri;
      return string.Join("/", relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
    }
  }
}
