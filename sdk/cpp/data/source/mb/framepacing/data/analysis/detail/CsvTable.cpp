// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "CsvTable.hpp"
#include <mb/framepacing/data/DataFormatError.hpp>
#include <fstream>
#include <stdexcept>
#include <string>

namespace MB::FramePacing::Data::Csv
{
  namespace
  {
    std::vector<std::string> Split(const std::string& line)
    {
      std::vector<std::string> cells;
      std::size_t start = 0;
      while (true)
      {
        const std::size_t comma = line.find(',', start);
        cells.push_back(line.substr(start, comma == std::string::npos ? std::string::npos : comma - start));
        if (comma == std::string::npos)
        {
          return cells;
        }
        start = comma + 1;
      }
    }
  }

  CsvTable CsvTable::Read(const std::filesystem::path& path)
  {
    std::ifstream file(path, std::ios::binary);
    if (!file)
    {
      throw std::runtime_error("Cannot open '" + path.string() + "'");
    }
    CsvTable table;
    table.Name = path.filename().string();
    std::string line;
    bool header = true;
    std::size_t lineNumber = 0;
    while (std::getline(file, line))
    {
      ++lineNumber;
      if (!line.empty() && line.back() == '\r')
      {
        line.pop_back();
      }
      if (header)
      {
        const auto names = Split(line);
        for (std::size_t i = 0; i < names.size(); ++i)
        {
          table.Columns[names[i]] = i;
        }
        header = false;
      }
      else if (!line.empty())
      {
        table.Rows.push_back(Split(line));
        table.Lines.push_back(lineNumber);
      }
    }
    if (header)
    {
      throw DataFormatError("'" + path.string() + "' is empty");
    }
    return table;
  }

  DataFormatError CsvTable::InRow(const std::size_t rowIndex, const DataFormatError& error) const
  {
    return DataFormatError("'" + Name + "' line " + std::to_string(Lines[rowIndex]) + ": " + error.what());
  }
}
