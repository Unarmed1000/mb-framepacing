//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run's rows as stretches of the capture's rows (RowRanges): the rows added by position are read back by index in any order, a run of
//* one stretch and one of many, and the last row matching a condition is found.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class RowRangesTests
  {
    private static List<CaptureRow> CaptureRows(int count) =>
      Enumerable.Range(0, count).Select(i => new CaptureRow(1000 + i, new NanosecondTickCount(i * 10L), CaptureStatus.Undecodable, default)).ToList();

    [Test]
    public void Rows_AreTheOnesAdded_InOrderAndInAnyOrder()
    {
      var capture = CaptureRows(200);
      var random = new Random(3);
      // One stretch, stretches with gaps, single rows apart
      foreach (var positions in new[] { Enumerable.Range(20, 100).ToArray(), new[] { 3, 4, 5, 9, 10, 40, 41, 42, 43, 199 }, new[] { 0, 2, 4, 6, 8 } })
      {
        var rows = new RowRanges(capture);
        foreach (int position in positions)
          rows.Add(position);

        Assert.That(rows.Count, Is.EqualTo(positions.Length));
        Assert.That(Enumerable.Range(0, rows.Count).Select(i => rows[i].CaptureIndex), Is.EqualTo(positions.Select(p => 1000L + p)));
        for (int i = 0; i < 300; ++i)
        {
          int index = random.Next(positions.Length);
          Assert.That(rows[index].CaptureIndex, Is.EqualTo(1000 + positions[index]));
        }
        Assert.That(rows.FindLastIndex(r => r.CaptureIndex < 1000 + positions[positions.Length / 2]), Is.EqualTo((positions.Length / 2) - 1));
        Assert.That(rows.FindLastIndex(r => r.CaptureIndex < 0), Is.EqualTo(-1));
      }
    }

    [Test]
    public void Rows_AreAddedInTheCapturesOrder_AndReadWithinTheirCount()
    {
      var rows = new RowRanges(CaptureRows(10));
      Assert.That(rows.Count, Is.Zero);
      Assert.That(rows.FindLastIndex(_ => true), Is.EqualTo(-1));
      rows.Add(4);
      rows.Add(5);

      Assert.Throws<ArgumentException>(() => rows.Add(5), "a row twice");
      Assert.Throws<ArgumentException>(() => rows.Add(2), "a row before the last");
      Assert.Throws<ArgumentOutOfRangeException>(() => rows.Add(10), "beyond the capture");
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = rows[2]);
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = rows[-1]);
    }
  }
}
