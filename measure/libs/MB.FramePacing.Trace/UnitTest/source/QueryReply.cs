//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A trace processor's reply to a query, made by a test: the bytes of Perfetto's QueryResult message for the rows given, in one batch or
//* a batch per row.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MB.FramePacing.Trace.UnitTest
{
  public static class QueryReply
  {
    /// <summary>The reply's bytes. A cell is a long, a double, a string or null.</summary>
    public static byte[] Bytes(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, bool batchPerRow = false, string? error = null)
    {
      var reply = new MemoryStream();
      foreach (string column in columns)
        Field(reply, 1, Encoding.UTF8.GetBytes(column));
      if (error != null)
        Field(reply, 2, Encoding.UTF8.GetBytes(error));
      if (batchPerRow)
      {
        foreach (var row in rows)
          Field(reply, 3, Batch(new[] { row }));
      }
      else
      {
        Field(reply, 3, Batch(rows));
      }
      return reply.ToArray();
    }

    public static TraceQueryResult Result(IReadOnlyList<string> columns, params object?[][] rows) =>
      TraceQueryResult.Decode(Bytes(columns, rows), "a test's query");

    private static byte[] Batch(IReadOnlyList<object?[]> rows)
    {
      var types = new MemoryStream();
      var wholes = new MemoryStream();
      var numbers = new MemoryStream();
      var texts = new MemoryStream();
      foreach (var row in rows)
      {
        foreach (var cell in row)
        {
          switch (cell)
          {
            case null:
              Varint(types, 1);
              break;
            case long whole:
              Varint(types, 2);
              Varint(wholes, unchecked((ulong)whole));
              break;
            case double number:
              Varint(types, 3);
              numbers.Write(BitConverter.GetBytes(number));
              break;
            case string text:
              Varint(types, 4);
              texts.Write(Encoding.UTF8.GetBytes(text));
              texts.WriteByte(0);
              break;
            default:
              throw new ArgumentException($"A cell of {cell.GetType().Name} is not one a reply has");
          }
        }
      }
      var batch = new MemoryStream();
      Field(batch, 1, types.ToArray());
      Field(batch, 2, wholes.ToArray());
      Field(batch, 3, numbers.ToArray());
      Field(batch, 5, texts.ToArray());
      // is_last_batch: a field the reader passes over
      Varint(batch, (6 << 3) | 0);
      Varint(batch, 1);
      return batch.ToArray();
    }

    private static void Field(Stream target, int field, byte[] bytes)
    {
      Varint(target, ((ulong)field << 3) | 2);
      Varint(target, (ulong)bytes.Length);
      target.Write(bytes);
    }

    private static void Varint(Stream target, ulong value)
    {
      while (value >= 0x80)
      {
        target.WriteByte((byte)(value | 0x80));
        value >>= 7;
      }
      target.WriteByte((byte)value);
    }
  }
}
