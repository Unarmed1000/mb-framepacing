//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A length of time in summary.json: a whole number of nanoseconds that is never negative (the "...Ns" fields), never a fraction or a
//* text, so a value is read back exactly as it was written. A negative number is refused, as any other value outside its field's range.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  internal sealed class NanosecondsJsonConverter : JsonConverter<NanosecondTimeDuration>
  {
    public override NanosecondTimeDuration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long nanoseconds) && nanoseconds >= 0
        ? NanosecondTimeDuration.FromNanoseconds(nanoseconds)
        : throw new JsonException("A length of time is a whole number of nanoseconds that is not negative");

    public override void Write(Utf8JsonWriter writer, NanosecondTimeDuration value, JsonSerializerOptions options) =>
      writer.WriteNumberValue(value.Nanoseconds);
  }
}
