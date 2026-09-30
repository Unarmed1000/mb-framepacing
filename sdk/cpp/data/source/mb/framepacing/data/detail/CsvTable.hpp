#ifndef MB_FRAMEPACING_DATA_DETAIL_CSVTABLE_HPP
#define MB_FRAMEPACING_DATA_DETAIL_CSVTABLE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: the CSV reading the frames CSV and captures.csv share.

#include <cstddef>
#include <filesystem>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace MB::FramePacing::Data::Detail
{
  //! The lines of a CSV file, split on commas, and its columns by name.
  // MSVC's std::unordered_map can throw bad_array_new_length inside its noexcept special members, and clang-tidy follows it there
  // NOLINTNEXTLINE(bugprone-exception-escape)
  struct CsvTable
  {
    std::unordered_map<std::string, std::size_t> Columns;
    std::vector<std::vector<std::string>> Rows;

    //! The row's cell in the named column; empty when the file has no such column.
    std::string_view Cell(const std::vector<std::string>& row, const char* name) const
    {
      const auto found = Columns.find(name);
      return found != Columns.end() && found->second < row.size() ? std::string_view(row[found->second]) : std::string_view();
    }

    //! Read a CSV file: its first line names the columns. Throws DataFormatError for an empty file.
    static CsvTable Read(const std::filesystem::path& path);
  };
}

#endif
