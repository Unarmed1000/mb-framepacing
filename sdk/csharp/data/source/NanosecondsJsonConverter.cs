//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A span of time in summary.json: a whole number of nanoseconds (the "...Ns" fields), never a fraction or a text, so a value is read
//* back exactly as it was written.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  internal sealed class NanosecondsJsonConverter : JsonConverter<NanosecondTimeSpan>
  {
    public override NanosecondTimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long nanoseconds)
        ? new NanosecondTimeSpan(nanoseconds)
        : throw new JsonException("A time is a whole number of nanoseconds");

    public override void Write(Utf8JsonWriter writer, NanosecondTimeSpan value, JsonSerializerOptions options) =>
      writer.WriteNumberValue(value.Nanoseconds);
  }
}
