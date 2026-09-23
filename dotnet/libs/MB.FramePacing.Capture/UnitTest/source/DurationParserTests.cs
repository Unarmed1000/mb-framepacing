//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture duration parsing.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class DurationParserTests
  {
    [TestCase("30s", 30.0)]
    [TestCase("1500ms", 1.5)]
    [TestCase("2m", 120.0)]
    [TestCase("1h", 3600.0)]
    [TestCase(" 2.5s ", 2.5)]
    [TestCase("00:00:30", 30.0)]
    public void Parse(string text, double seconds)
    {
      Assert.That(DurationParser.Parse(text), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("-5s")]
    [TestCase("0s")]
    public void Parse_RejectsGarbage(string text)
    {
      Assert.Throws<FormatException>(() => DurationParser.Parse(text));
    }

    [Test]
    public void ParseOptional_EmptyMeansNoLimit()
    {
      Assert.That(DurationParser.ParseOptional(null), Is.Null);
      Assert.That(DurationParser.ParseOptional("  "), Is.Null);
      Assert.That(DurationParser.ParseOptional("10s"), Is.EqualTo(TimeSpan.FromSeconds(10)));
    }
  }
}
