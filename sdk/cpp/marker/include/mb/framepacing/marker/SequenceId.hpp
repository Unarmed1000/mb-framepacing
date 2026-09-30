#ifndef MB_FRAMEPACING_MARKER_SEQUENCEID_HPP
#define MB_FRAMEPACING_MARKER_SEQUENCEID_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace MB::FramePacing::Marker
{
  //! The start marker's sequence id: 16 opaque bytes that identify the capture sequence, any content as long as it is unique to it (a
  //! UUID's bytes, or a short text tag padded with zeros). The wire format carries Bytes in order, byte 0 first.
  struct SequenceId
  {
    static constexpr std::size_t ByteCount = 16;

    std::array<uint8_t, ByteCount> Bytes{};

    //! All zero: no sequence id.
    [[nodiscard]] constexpr bool IsEmpty() const noexcept
    {
      for (const uint8_t value : Bytes)
      {
        if (value != 0u)
        {
          return false;
        }
      }
      return true;
    }

    //! A text tag of 1 to 16 printable ASCII characters (0x20..0x7E), padded with zero bytes. Returns false (and leaves rId unchanged)
    //! for any other text.
    static constexpr bool TryFromText(const std::string_view text, SequenceId& rId) noexcept
    {
      if (text.empty() || text.size() > ByteCount)
      {
        return false;
      }
      SequenceId id;
      for (std::size_t i = 0; i < text.size(); ++i)
      {
        const auto value = static_cast<uint8_t>(text[i]);
        if (value < 0x20u || value > 0x7Eu)
        {
          return false;
        }
        id.Bytes[i] = value;
      }
      rId = id;
      return true;
    }

    constexpr bool operator==(const SequenceId&) const noexcept = default;
  };
}

#endif
