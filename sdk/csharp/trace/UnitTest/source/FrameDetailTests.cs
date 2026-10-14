//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame record's second part (optional, not checked against the capture): the names of its slices and moments against the golden data
//* both languages read (sdk/test-data/trace/frame-details.csv), parsed back, and its strings as the document has them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class FrameDetailTests
  {
    private static string GoldenFile()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "trace", "frame-details.csv")))
        directory = directory.Parent;
      return Path.Combine(
        directory?.FullName ?? throw new DirectoryNotFoundException("test-data/trace not found above the test directory"),
        "test-data",
        "trace",
        "frame-details.csv"
      );
    }

    [Test]
    public void EveryDetailIsNamedAsTheGoldenDataHasIt_AndParsedBack()
    {
      var lines = File.ReadAllLines(GoldenFile()).Where(line => line.Length > 0).ToArray();
      Assert.That(lines[0], Is.EqualTo("detail,runId,frameIndex,name"));
      foreach (string line in lines.Skip(1))
      {
        string[] columns = line.Split(',', 4);
        var detail = Enum.Parse<FrameDetail>(columns[0]);
        uint runId = uint.Parse(columns[1], CultureInfo.InvariantCulture);
        ulong frameIndex = ulong.Parse(columns[2], CultureInfo.InvariantCulture);

        string name = FrameDetailName.Format(detail, runId, frameIndex);

        Assert.That(name, Is.EqualTo(columns[3]), line);
        Assert.That(name.Length, Is.LessThanOrEqualTo(FrameDetailName.MaxLength), line);
        Assert.That(FrameDetailName.TryParse(name, out var parsedDetail, out uint parsedRun, out ulong parsedIndex), Is.True, line);
        Assert.That((parsedDetail, parsedRun, parsedIndex), Is.EqualTo((detail, runId, frameIndex)), line);
        // It is no frame record
        Assert.That(FrameRecordName.TryParse(name, out _, out _), Is.False, line);
      }
      Assert.That(lines.Length - 1, Is.EqualTo(5), "a row per detail");
    }

    [Test]
    public void TheLongestNameFits_AndWhatIsNoDetailHasNoName()
    {
      Assert.That(FrameDetailName.Format(FrameDetail.NotPresented, uint.MaxValue, ulong.MaxValue).Length, Is.EqualTo(FrameDetailName.MaxLength));
      Assert.That(FrameDetailName.Format((FrameDetail)9, 1, 2), Is.Empty);
      Assert.That(FrameDetailName.PrefixOf((FrameDetail)9), Is.Empty);
      Span<char> small = stackalloc char[10];
      Assert.That(FrameDetailName.TryFormat(FrameDetail.Present, 1, 2, small, out int written), Is.False);
      Assert.That(written, Is.Zero);
      // A name that is not of the second part, or whose ids are not numbers
      Assert.That(FrameDetailName.TryParse("mbfp frame 1 2", out _, out _, out _), Is.False);
      Assert.That(FrameDetailName.TryParse("mbfp present x 2", out _, out _, out _), Is.False);
      Assert.That(FrameDetailName.TryParse("mbfp present 1", out _, out _, out _), Is.False);
    }

    [Test]
    public void ThePartTwoStringsAreTheDocumentsAndNoneIsTheRecords()
    {
      Assert.That(FrameRecordText.PresentTrackName, Is.EqualTo("mbfp presents"));
      Assert.That(FrameRecordText.EnvironmentName, Is.EqualTo("mbfp environment"));
      Assert.That(FrameRecordText.FramesInFlight, Is.EqualTo("framesInFlight"));
      Assert.That(FrameRecordText.PresentModeQueueLateAtOnce, Is.EqualTo("queue, late at once"));
      Assert.That(FrameRecordText.PlatformFeatures, Is.EqualTo("platformFeatures"));
      foreach (
        string name in new[]
        {
          FrameRecordText.WaitNamePrefix,
          FrameRecordText.PresentNamePrefix,
          FrameRecordText.GpuNamePrefix,
          FrameRecordText.DisplayNamePrefix,
          FrameRecordText.NotPresentedNamePrefix,
          FrameRecordText.EnvironmentName,
          FrameRecordText.SwapChainRecreatedName,
          FrameRecordText.SwapIntervalChangeName,
        }
      )
      {
        Assert.That(name.StartsWith(FrameRecordText.NamePrefix, StringComparison.Ordinal), Is.False, name);
        Assert.That(name.StartsWith("mbfp ", StringComparison.Ordinal), Is.True, name);
      }
    }
  }
}
