//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The rows of one query to Perfetto's trace processor, decoded from its reply (Perfetto's QueryResult message: the column names, an
//* error text, and batches of cells; a batch has each cell's type in row order and the cells' values by type, the texts one after the
//* other with a zero byte after each).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The rows a query to the trace processor gave: whole numbers, numbers, texts and empty cells.</summary>
  public sealed class TraceQueryResult
  {
    // Perfetto's field numbers (protos/perfetto/trace_processor/trace_processor.proto: QueryResult, QueryResult.CellsBatch)
    private const int ResultColumnNames = 1;
    private const int ResultError = 2;
    private const int ResultBatch = 3;
    private const int BatchCells = 1;
    private const int BatchVarintCells = 2;
    private const int BatchFloat64Cells = 3;
    private const int BatchBlobCells = 4;
    private const int BatchStringCells = 5;

    private const int CellNull = 1;
    private const int CellVarint = 2;
    private const int CellFloat64 = 3;
    private const int CellString = 4;
    private const int CellBlob = 5;

    private readonly List<object?> m_cells;

    private TraceQueryResult(IReadOnlyList<string> columns, List<object?> cells)
    {
      Columns = columns;
      m_cells = cells;
      RowCount = columns.Count == 0 ? 0 : cells.Count / columns.Count;
    }

    public IReadOnlyList<string> Columns { get; }

    public int RowCount { get; }

    /// <summary>
    /// Decode a reply. A reply that has an error text, or bytes that are not a reply, is a <see cref="TraceException"/> naming
    /// <paramref name="sql"/>.
    /// </summary>
    public static TraceQueryResult Decode(ReadOnlySpan<byte> reply, string sql)
    {
      try
      {
        var columns = new List<string>();
        var cells = new List<object?>();
        string error = string.Empty;
        var reader = new ProtoReader(reply);
        while (reader.Next())
        {
          switch (reader.Field)
          {
            case ResultColumnNames when reader.WireType == ProtoReader.WireLength:
              columns.Add(Encoding.UTF8.GetString(reader.Bytes()));
              break;
            case ResultError when reader.WireType == ProtoReader.WireLength:
              error = Encoding.UTF8.GetString(reader.Bytes());
              break;
            case ResultBatch when reader.WireType == ProtoReader.WireLength:
              DecodeBatch(reader.Bytes(), cells);
              break;
            default:
              reader.Skip();
              break;
          }
        }
        if (error.Length > 0)
          throw new TraceException($"The trace processor refused a query ({error}): {sql}");
        if (columns.Count > 0 && cells.Count % columns.Count != 0)
          throw new InvalidDataException($"{cells.Count} cells do not fill rows of {columns.Count} columns");
        return new TraceQueryResult(columns, cells);
      }
      catch (InvalidDataException ex)
      {
        throw new TraceException($"The trace processor's reply to a query could not be read ({ex.Message}): {sql}", ex);
      }
    }

    /// <summary>A whole number; an empty cell or another kind of value is an error.</summary>
    public long Whole(int row, int column) =>
      WholeOrNull(row, column) ?? throw new TraceException($"The trace processor gave no value for '{Columns[column]}' in row {row}.");

    public long? WholeOrNull(int row, int column) =>
      Cell(row, column) switch
      {
        null => null,
        long value => value,
        // SQL gives a sum or a product as a number when it is one: a whole one is taken as it is
        double value when value == Math.Floor(value) && Math.Abs(value) < 9.0e18 => (long)value,
        var other => throw new TraceException($"The trace processor gave '{other}' for '{Columns[column]}', which is not a whole number."),
      };

    public double Number(int row, int column) =>
      Cell(row, column) switch
      {
        long value => value,
        double value => value,
        var other => throw new TraceException($"The trace processor gave '{other}' for '{Columns[column]}', which is not a number."),
      };

    /// <summary>A text; an empty cell is null.</summary>
    public string? TextOrNull(int row, int column) =>
      Cell(row, column) switch
      {
        null => null,
        string value => value,
        var other => throw new TraceException($"The trace processor gave '{other}' for '{Columns[column]}', which is not a text."),
      };

    public string Text(int row, int column) => TextOrNull(row, column) ?? string.Empty;

    private object? Cell(int row, int column) => m_cells[(row * Columns.Count) + column];

    private static void DecodeBatch(ReadOnlySpan<byte> batch, List<object?> cells)
    {
      var types = new List<int>();
      var wholes = new List<long>();
      var numbers = new List<double>();
      var texts = new List<string>();
      int blobs = 0;
      var reader = new ProtoReader(batch);
      while (reader.Next())
      {
        switch (reader.Field)
        {
          case BatchCells when reader.WireType == ProtoReader.WireLength:
          {
            var packed = new ProtoReader(reader.Bytes());
            while (!packed.AtEnd)
              types.Add(checked((int)packed.Varint()));
            break;
          }
          case BatchCells when reader.WireType == ProtoReader.WireVarint:
            types.Add(checked((int)reader.Varint()));
            break;
          case BatchVarintCells when reader.WireType == ProtoReader.WireLength:
          {
            var packed = new ProtoReader(reader.Bytes());
            while (!packed.AtEnd)
              wholes.Add(unchecked((long)packed.Varint()));
            break;
          }
          case BatchVarintCells when reader.WireType == ProtoReader.WireVarint:
            wholes.Add(unchecked((long)reader.Varint()));
            break;
          case BatchFloat64Cells when reader.WireType == ProtoReader.WireLength:
          {
            var packed = new ProtoReader(reader.Bytes());
            while (!packed.AtEnd)
              numbers.Add(BitConverter.UInt64BitsToDouble(packed.Fixed64()));
            break;
          }
          case BatchFloat64Cells when reader.WireType == ProtoReader.WireFixed64:
            numbers.Add(BitConverter.UInt64BitsToDouble(reader.Fixed64()));
            break;
          case BatchBlobCells when reader.WireType == ProtoReader.WireLength:
            reader.Bytes();
            ++blobs;
            break;
          case BatchStringCells when reader.WireType == ProtoReader.WireLength:
          {
            // Every text ends in a zero byte
            var all = reader.Bytes();
            while (!all.IsEmpty)
            {
              int end = all.IndexOf((byte)0);
              if (end < 0)
                end = all.Length;
              texts.Add(Encoding.UTF8.GetString(all[..end]));
              all = end < all.Length ? all[(end + 1)..] : ReadOnlySpan<byte>.Empty;
            }
            break;
          }
          default:
            reader.Skip();
            break;
        }
      }

      int whole = 0;
      int number = 0;
      int text = 0;
      int blob = 0;
      foreach (int type in types)
      {
        switch (type)
        {
          case CellNull:
            cells.Add(null);
            break;
          case CellVarint when whole < wholes.Count:
            cells.Add(wholes[whole++]);
            break;
          case CellFloat64 when number < numbers.Count:
            cells.Add(numbers[number++]);
            break;
          case CellString when text < texts.Count:
            cells.Add(texts[text++]);
            break;
          case CellBlob when blob < blobs:
            // Nothing the tools ask for is bytes: the cell is kept as an empty one
            ++blob;
            cells.Add(null);
            break;
          default:
            throw new InvalidDataException($"A batch of cells has a cell of type {type} without a value");
        }
      }
    }
  }
}
