//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Point: a pixel position that compares by value (the same cases as the C++ core's tests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class PointTests
  {
    [Test]
    public void IsAPixelPosition_ThatComparesByValue()
    {
      Assert.That(default(Point), Is.EqualTo(new Point(0, 0)));
      var point = new Point(3, -4);
      Assert.That((point.X, point.Y), Is.EqualTo((3, -4)));
      Assert.That(point == new Point(3, -4), Is.True);
      Assert.That(point != new Point(-4, 3), Is.True);
      Assert.That(point.ToString(), Is.EqualTo("{3,-4}"));
      Assert.That(point.Equals(new Point(3, -5)), Is.False);
      Assert.That(point.Equals(new Point(4, -4)), Is.False);
      Assert.That(point.Equals((object)new Point(3, -4)), Is.True);
      Assert.That(point.Equals("{3,-4}"), Is.False);
      Assert.That(point.GetHashCode(), Is.EqualTo(new Point(3, -4).GetHashCode()));
      Assert.That(point.GetHashCode(), Is.Not.EqualTo(new Point(-4, 3).GetHashCode()));
    }
  }
}
