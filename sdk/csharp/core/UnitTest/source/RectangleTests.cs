//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Rectangle: its edges, and that it is always valid (the same cases as the C++ core's tests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class RectangleTests
  {
    [Test]
    public void ItsEdgesFollowFromItsPositionAndSize()
    {
      var rect = new Rectangle(10, 20, 30, 40);
      Assert.That((rect.X, rect.Y, rect.Width, rect.Height), Is.EqualTo((10, 20, 30, 40)));
      Assert.That((rect.Left, rect.Top, rect.Right, rect.Bottom), Is.EqualTo((10, 20, 40, 60)));
      Assert.That(rect.Contains(10, 20) && rect.Contains(39, 59), Is.True);
      Assert.That(rect.Contains(40, 20) || rect.Contains(10, 60) || rect.Contains(9, 20), Is.False);
      Assert.That(Rectangle.FromLeftTopRightBottom(10, 20, 40, 60), Is.EqualTo(rect));
      Assert.That(Rectangle.Empty, Is.EqualTo(new Rectangle(0, 0, 0, 0)));
      Assert.That(rect.ToString(), Is.EqualTo("10,20,30,40"));
    }

    [Test]
    public void ItIsAlwaysValid()
    {
      // A negative size is 0
      Assert.That(new Rectangle(5, 6, -3, -4), Is.EqualTo(new Rectangle(5, 6, 0, 0)));
      Assert.That(Rectangle.FromLeftTopRightBottom(40, 60, 10, 20), Is.EqualTo(new Rectangle(40, 60, 0, 0)));
      Assert.That(new Rectangle(0, 0, 7, -1).Height, Is.Zero);
      Assert.That(new Rectangle(5, 6, 0, 7).IsEmpty, Is.True);
      Assert.That(new Rectangle(5, 6, 7, 0).IsEmpty, Is.True);
      Assert.That(new Rectangle(5, 6, 7, 8).IsEmpty, Is.False);
    }

    [Test]
    public void EveryEdgeIsCheckedByContains()
    {
      var rect = new Rectangle(10, 20, 30, 40);
      Assert.That(rect.Contains(10, 20), Is.True);
      Assert.That(rect.Contains(39, 59), Is.True);
      Assert.That(rect.Contains(9, 20), Is.False);
      Assert.That(rect.Contains(40, 20), Is.False);
      Assert.That(rect.Contains(10, 19), Is.False);
      Assert.That(rect.Contains(10, 60), Is.False);
    }

    [Test]
    public void ComparesByValue_AndDeconstructs()
    {
      var rect = new Rectangle(10, 20, 30, 40);
      Assert.That(rect == new Rectangle(10, 20, 30, 40), Is.True);
      Assert.That(rect != new Rectangle(10, 20, 30, 41), Is.True);
      Assert.That(rect.Equals(new Rectangle(11, 20, 30, 40)), Is.False);
      Assert.That(rect.Equals(new Rectangle(10, 21, 30, 40)), Is.False);
      Assert.That(rect.Equals(new Rectangle(10, 20, 31, 40)), Is.False);
      Assert.That(rect.Equals((object)new Rectangle(10, 20, 30, 40)), Is.True);
      Assert.That(rect.Equals("10,20,30,40"), Is.False);
      Assert.That(rect.GetHashCode(), Is.EqualTo(new Rectangle(10, 20, 30, 40).GetHashCode()));
      Assert.That(rect.GetHashCode(), Is.Not.EqualTo(new Rectangle(10, 20, 30, 41).GetHashCode()));
      (int x, int y, int width, int height) = rect;
      Assert.That((x, y, width, height), Is.EqualTo((10, 20, 30, 40)));
    }
  }
}
