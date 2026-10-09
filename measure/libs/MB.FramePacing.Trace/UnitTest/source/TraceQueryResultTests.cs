//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The trace processor's replies, from bytes a test makes (QueryReply): rows of whole numbers, numbers, texts and empty cells in one batch
//* or several, a reply with an error text, and bytes that are no reply.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class TraceQueryResultTests
  {
    private static readonly string[] g_columns = { "ts", "value", "name" };

    private static readonly object?[][] g_rows =
    {
      new object?[] { 5L, 1.5, "first" },
      new object?[] { -7L, null, "é,\"x\"" },
      new object?[] { long.MaxValue, 3.0, null },
      new object?[] { null, -0.25, string.Empty },
    };

    [TestCase(false)]
    [TestCase(true)]
    public void Rows_AreDecodedFromOneBatchOrSeveral(bool batchPerRow)
    {
      var result = TraceQueryResult.Decode(QueryReply.Bytes(g_columns, g_rows, batchPerRow), "sql");

      Assert.That(result.Columns, Is.EqualTo(g_columns));
      Assert.That(result.RowCount, Is.EqualTo(4));
      Assert.That(result.Whole(0, 0), Is.EqualTo(5));
      Assert.That(result.Number(0, 1), Is.EqualTo(1.5));
      Assert.That(result.Text(0, 2), Is.EqualTo("first"));
      Assert.That(result.Whole(1, 0), Is.EqualTo(-7));
      Assert.That(result.TextOrNull(1, 2), Is.EqualTo("é,\"x\""));
      Assert.That(result.Whole(2, 0), Is.EqualTo(long.MaxValue));
      Assert.That(result.TextOrNull(2, 2), Is.Null);
      Assert.That(result.Text(2, 2), Is.Empty);
      Assert.That(result.WholeOrNull(3, 0), Is.Null);
      Assert.That(result.TextOrNull(3, 2), Is.Empty);
      // A number that is whole reads as one (SQL gives a sum as a number), a whole number as a number
      Assert.That(result.Whole(2, 1), Is.EqualTo(3));
      Assert.That(result.Number(0, 0), Is.EqualTo(5.0));
    }

    [Test]
    public void ACellOfAnotherKind_IsAnError()
    {
      var result = TraceQueryResult.Decode(QueryReply.Bytes(g_columns, g_rows), "sql");

      Assert.That(Assert.Throws<TraceException>(() => result.Whole(3, 0))!.Message, Does.Contain("'ts'"));
      Assert.That(Assert.Throws<TraceException>(() => result.Whole(0, 1))!.Message, Does.Contain("not a whole number"));
      Assert.That(Assert.Throws<TraceException>(() => result.Whole(0, 2))!.Message, Does.Contain("not a whole number"));
      Assert.That(Assert.Throws<TraceException>(() => result.Number(0, 2))!.Message, Does.Contain("not a number"));
      Assert.That(Assert.Throws<TraceException>(() => result.Number(1, 1))!.Message, Does.Contain("not a number"));
      Assert.That(Assert.Throws<TraceException>(() => result.TextOrNull(0, 0))!.Message, Does.Contain("not a text"));
    }

    [Test]
    public void AReplyWithoutRows_HasNone()
    {
      Assert.That(QueryReply.Result(g_columns).RowCount, Is.Zero);
      Assert.That(TraceQueryResult.Decode(ReadOnlySpan<byte>.Empty, "sql").RowCount, Is.Zero);
    }

    [Test]
    public void AReplyWithAnError_IsRefusedWithTheQuery()
    {
      var error = Assert.Throws<TraceException>(() =>
        TraceQueryResult.Decode(QueryReply.Bytes(g_columns, Array.Empty<object?[]>(), error: "no such table: x"), "SELECT 1 FROM x")
      );

      Assert.That(error!.Message, Does.Contain("no such table: x").And.Contain("SELECT 1 FROM x"));
    }

    [TestCase("0A05AA")] // a column name longer than the reply
    [TestCase("1A040A020405")] // a batch with two texts' types and no text
    [TestCase("0A01610A01621A030A0102")] // two columns and one cell
    public void BytesThatAreNoReply_AreRefused(string hex)
    {
      var error = Assert.Throws<TraceException>(() => TraceQueryResult.Decode(Convert.FromHexString(hex), "sql"));

      Assert.That(error!.Message, Does.Contain("could not be read"));
    }

    [Test]
    public void Quoted_IsAnSqlString()
    {
      Assert.That(TraceReader.Quoted("plain"), Is.EqualTo("'plain'"));
      Assert.That(TraceReader.Quoted("the pacer's"), Is.EqualTo("'the pacer''s'"));
    }
  }
}
