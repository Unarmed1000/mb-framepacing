// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Stress test of the marker's QR encoder against the reference (the vendored qrcodegen): far more input than the unit tests give it, to
// show that both keep producing the same symbols. Payloads of many kinds on every core, every mask and its penalty score for a share of
// them, GenerateModules with its packing, drawn symbols, and the line scoring for every line of 25 modules there is.
//
//   mb_framepacing_marker_qr_stress [--payloads N] [--lines N] [--symbols N] [--threads N] [--seed N] [--no-exhaustive]
//
// The run is deterministic for a seed and a thread count. It stops at the first difference, prints what differed and exits with 1.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <functional>
#include <mutex>
#include <span>
#include <string>
#include <string_view>
#include <thread>
#include <vector>
#include "QrcodegenReference.h"
#include "mb/framepacing/marker/detail/QrEncoder.hpp"
#include "mb/framepacing/marker/detail/QrSymbol.hpp"

namespace FM = MB::FramePacing::Marker;
namespace FP = MB::FramePacing;
namespace QR = MB::FramePacing::Marker::QrEncoder;

namespace
{
  using ReferenceSymbol = std::array<uint8_t, qrcodegen_BUFFER_LEN_FOR_VERSION(6)>;

  struct Settings
  {
    uint64_t Payloads{2'000'000};
    uint64_t Lines{200'000'000};
    uint64_t Symbols{400'000};
    uint32_t Threads{std::max(1u, std::thread::hardware_concurrency())};
    uint64_t Seed{0x9E3779B97F4A7C15u};
    bool Exhaustive{true};
  };

  //! xorshift64*: fast, and the same numbers everywhere.
  class Random
  {
    uint64_t m_state;

  public:
    explicit Random(const uint64_t seed)
      : m_state(seed != 0 ? seed : 1)
    {
    }

    uint64_t Next()
    {
      m_state ^= m_state >> 12u;
      m_state ^= m_state << 25u;
      m_state ^= m_state >> 27u;
      return m_state * 0x2545F4914F6CDD1Du;
    }

    //! 0 to bound - 1.
    uint64_t Below(const uint64_t bound)
    {
      return Next() % bound;
    }
  };

  std::atomic<bool> g_failed{false};
  std::mutex g_reportMutex;

  std::string Hex(const std::span<const uint8_t> bytes)
  {
    std::string text;
    for (const uint8_t value : bytes)
    {
      constexpr std::string_view Digits = "0123456789abcdef";
      text.push_back(Digits[value >> 4u]);
      text.push_back(Digits[value & 15u]);
    }
    return text;
  }

  //! The first difference of a run: printed once, and every thread stops.
  void Fail(const std::string& what)
  {
    const std::scoped_lock lock(g_reportMutex);
    if (!g_failed.exchange(true))
    {
      std::fprintf(stderr, "DIFFERENCE: %s\n", what.c_str());
    }
  }

  constexpr std::size_t CapacityOf(const int32_t version)
  {
    return version == 2 ? 26u : 106u;
  }

  bool ReferenceEncode(const std::span<const uint8_t> data, const int32_t version, const qrcodegen_Mask mask, ReferenceSymbol& rSymbol)
  {
    ReferenceSymbol dataAndTemp{};
    std::copy(data.begin(), data.end(), dataAndTemp.begin());
    return qrcodegen_encodeBinary(dataAndTemp.data(), data.size(), rSymbol.data(), qrcodegen_Ecc_MEDIUM, version, version, mask, false);
  }

  //! Both encoders hold the same symbol, and ours holds it the same as rows and as columns.
  bool SameSymbol(const QR::QrSymbol& symbol, const ReferenceSymbol& reference, std::string& rWhy)
  {
    const int32_t size = qrcodegen_getSize(reference.data());
    if (symbol.Size != size)
    {
      rWhy = "size " + std::to_string(symbol.Size) + ", the reference's is " + std::to_string(size);
      return false;
    }
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        const bool expected = qrcodegen_getModule(reference.data(), x, y);
        const bool inColumn = (symbol.Columns[static_cast<std::size_t>(x)] & QR::QrSymbol::Bit(y)) != 0u;
        if (symbol.IsDark(x, y) != expected || inColumn != expected)
        {
          rWhy = "module (" + std::to_string(x) + ", " + std::to_string(y) + ") is " + (expected ? "dark" : "light") + " in the reference";
          return false;
        }
      }
    }
    return true;
  }

  //! The next payload: one of several kinds, some of them made from the one before.
  void NextPayload(Random& rRandom, const int32_t version, std::vector<uint8_t>& rPayload)
  {
    const std::size_t capacity = CapacityOf(version);
    const uint64_t kind = rRandom.Below(10);
    if (kind >= 7 && !rPayload.empty() && rPayload.size() <= capacity)
    {
      // The payload before, a little different: one bit, one byte, or one byte more or less
      const std::size_t at = rRandom.Below(rPayload.size());
      if (kind == 7)
      {
        rPayload[at] = static_cast<uint8_t>(rPayload[at] ^ (1u << rRandom.Below(8)));
      }
      else if (kind == 8)
      {
        rPayload[at] = static_cast<uint8_t>(rRandom.Next());
      }
      else if (rPayload.size() < capacity && (rRandom.Next() & 1u) != 0u)
      {
        rPayload.push_back(static_cast<uint8_t>(rRandom.Next()));
      }
      else
      {
        rPayload.pop_back();
      }
      return;
    }

    // The lengths at the ends more often than their share
    const uint64_t lengthKind = rRandom.Below(8);
    const std::size_t length = lengthKind == 0 ? 0 : lengthKind == 1 ? capacity : lengthKind == 2 ? capacity - 1 : rRandom.Below(capacity + 1);
    rPayload.resize(length);
    const uint64_t period = 1 + rRandom.Below(8);
    const auto fill = static_cast<uint8_t>(rRandom.Next());
    for (std::size_t i = 0; i < length; ++i)
    {
      const auto value = static_cast<uint8_t>(rRandom.Next());
      switch (kind)
      {
      case 0:    // any bytes
      case 1:
        rPayload[i] = value;
        break;
      case 2:    // mostly zero, as a marker's bytes are
        rPayload[i] = rRandom.Below(6) == 0 ? value : uint8_t{0};
        break;
      case 3:    // mostly 0xFF
        rPayload[i] = rRandom.Below(6) == 0 ? value : uint8_t{0xFF};
        break;
      case 4:    // one byte, repeated
        rPayload[i] = fill;
        break;
      case 5:    // a short pattern, repeated
        rPayload[i] = static_cast<uint8_t>(fill + (static_cast<uint8_t>(i % period) * 37u));
        break;
      default:    // few bits set
        rPayload[i] = static_cast<uint8_t>(value & static_cast<uint8_t>(rRandom.Next()) & static_cast<uint8_t>(rRandom.Next()));
        break;
      }
    }
  }

  //! Encode with both encoders and compare; every eighth payload with each of the eight masks and its penalty score too.
  bool ComparePayload(const std::span<const uint8_t> payload, const int32_t version, const bool everyMask)
  {
    ReferenceSymbol reference{};
    QR::QrSymbol symbol;
    std::string why;
    const bool referenceEncoded = ReferenceEncode(payload, version, qrcodegen_Mask_AUTO, reference);
    const bool encoded = QR::Encode(payload, version, symbol);
    if (encoded != referenceEncoded || (encoded && !SameSymbol(symbol, reference, why)))
    {
      Fail("version " + std::to_string(version) + ", payload " + Hex(payload) + ": " +
           (encoded != referenceEncoded ? "only one encoder took it" : why));
      return false;
    }
    for (int32_t mask = 0; everyMask && mask < 8; ++mask)
    {
      const bool referenceMasked = ReferenceEncode(payload, version, static_cast<qrcodegen_Mask>(mask), reference);
      const bool masked = QR::EncodeWithMask(payload, version, mask, symbol);
      if (masked != referenceMasked || !SameSymbol(symbol, reference, why))
      {
        Fail("version " + std::to_string(version) + ", mask " + std::to_string(mask) + ", payload " + Hex(payload) + ": " + why);
        return false;
      }
      const long expected = QrcodegenReference_PenaltyScore(reference.data());
      if (QR::PenaltyScore(symbol) != expected)
      {
        Fail("version " + std::to_string(version) + ", mask " + std::to_string(mask) + ", payload " + Hex(payload) + ": penalty " +
             std::to_string(QR::PenaltyScore(symbol)) + ", the reference's is " + std::to_string(expected));
        return false;
      }
    }
    return true;
  }

  //! A marker as an application makes it: GenerateModules' packed modules against the reference's symbol of the same payload bytes.
  bool CompareMarker(Random& rRandom, uint64_t& rFrameIndex)
  {
    const uint64_t kindChoice = rRandom.Below(8);
    const FM::MarkerKind kind = kindChoice == 0   ? FM::MarkerKind::SequenceStart
                                : kindChoice == 1 ? FM::MarkerKind::SequenceEnd
                                : kindChoice <= 3 ? FM::MarkerKind::Sync
                                                  : FM::MarkerKind::Frame;
    // A running frame index and times, or anything at all
    const bool running = rRandom.Below(4) != 0;
    rFrameIndex = running ? rFrameIndex + 1 : rRandom.Next();
    const auto time = static_cast<int64_t>(running ? rFrameIndex * 16'666'667u : rRandom.Next());
    const FM::Payload payload(
      kind, static_cast<uint32_t>(running ? 0x12345678u : rRandom.Next()), rFrameIndex,
      static_cast<FM::MarkerFlags>(running ? rRandom.Below(4) : rRandom.Below(256)), FP::NanosecondTimeSpan(running ? time : time / 4),
      FP::NanosecondTimeDuration::FromNanoseconds(static_cast<uint32_t>(running ? 16'666'667u : rRandom.Next())),
      FP::NanosecondTimeDuration::FromNanoseconds(static_cast<uint32_t>(running ? 16'666'667u : rRandom.Next())),
      FP::NanosecondTickCount(running ? 3'600'000'000'000 + time : time), FP::NanosecondTickCount(running ? 3'599'990'000'000 + time : time / 2),
      FP::NanosecondTimeDuration::FromNanoseconds(static_cast<uint32_t>(rRandom.Below(40'000'000))));
    FM::StartMetadata metadata;
    metadata.UtcTicks = static_cast<int64_t>(rRandom.Next() >> 2u);
    for (uint8_t& rByte : metadata.Id.Bytes)
    {
      rByte = static_cast<uint8_t>(rRandom.Next());
    }

    std::array<uint8_t, FM::Payload::MaxEncodedByteCount> bytes{};
    const std::size_t byteCount = FM::EncodePayload(payload, metadata, bytes);
    FM::ModuleMatrix matrix;
    ReferenceSymbol reference{};
    const int32_t version = kind == FM::MarkerKind::Sync ? 2 : 6;
    if (byteCount == 0 || !FM::GenerateModules(payload, matrix, metadata) ||
        !ReferenceEncode(std::span<const uint8_t>(bytes).first(byteCount), version, qrcodegen_Mask_AUTO, reference))
    {
      Fail("a marker was not encoded: payload bytes " + Hex(std::span<const uint8_t>(bytes).first(byteCount)));
      return false;
    }
    const int32_t size = qrcodegen_getSize(reference.data());
    bool same = matrix.Size() == size;
    for (int32_t y = 0; same && y < size; ++y)
    {
      for (int32_t x = 0; same && x < size; ++x)
      {
        same = matrix.IsDark(x, y) == qrcodegen_getModule(reference.data(), x, y);
      }
    }
    // The bytes past the last module are zero
    const std::span<const uint8_t> bits = matrix.Bits();
    same = same && (bits.back() & 0x7Fu) == 0u;
    if (!same)
    {
      Fail("GenerateModules differs from the reference: payload bytes " + Hex(std::span<const uint8_t>(bytes).first(byteCount)));
    }
    return same;
  }

  //! The penalty of a line as the reference scores a row: module by module, with its run history.
  int32_t LinePenaltyByModules(const uint64_t line, const int32_t size)
  {
    std::array<int32_t, 7> history{};
    const auto addRun = [&history, size](int32_t length)
    {
      if (history[0] == 0)
      {
        length += size;
      }
      std::copy_backward(history.begin(), history.end() - 1, history.end());
      history[0] = length;
    };
    const auto countPatterns = [&history]()
    {
      const int32_t n = history[1];
      const bool core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
      return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
    };

    int32_t result = 0;
    bool runColor = false;
    int32_t run = 0;
    for (int32_t x = 0; x < size; ++x)
    {
      const bool dark = (line & QR::QrSymbol::Bit(x)) != 0u;
      if (dark == runColor)
      {
        ++run;
        if (run == 5)
        {
          result += 3;
        }
        else if (run > 5)
        {
          ++result;
        }
      }
      else
      {
        addRun(run);
        if (!runColor)
        {
          result += countPatterns() * 40;
        }
        runColor = dark;
        run = 1;
      }
    }
    if (runColor)
    {
      addRun(run);
      run = 0;
    }
    addRun(run + size);
    return result + (countPatterns() * 40);
  }

  bool CompareLine(const uint64_t line, const int32_t size)
  {
    const int32_t expected = LinePenaltyByModules(line, size);
    const int32_t actual = QR::LinePenalty(line, size);
    if (actual != expected)
    {
      Fail("a line of " + std::to_string(size) + " modules, bits " + std::to_string(line >> static_cast<uint32_t>(64 - size)) + ": penalty " +
           std::to_string(actual) + ", module by module it is " + std::to_string(expected));
    }
    return actual == expected;
  }

  //! A line of runs: lengths that make finder-like patterns likely (1, 1, 3, 1, 1 times a unit), long runs, or anything.
  uint64_t StructuredLine(Random& rRandom, const int32_t size)
  {
    uint64_t line = 0;
    int32_t position = 0;
    bool dark = (rRandom.Next() & 1u) != 0u;
    const auto unit = static_cast<int32_t>(1 + rRandom.Below(5));
    const uint64_t kind = rRandom.Below(4);
    while (position < size)
    {
      constexpr std::array<int32_t, 8> FinderRuns = {1, 1, 3, 1, 1, 4, 1, 2};
      int32_t length = 0;
      switch (kind)
      {
      case 0:
        length = FinderRuns[rRandom.Below(FinderRuns.size())] * unit;
        break;
      case 1:
        length = static_cast<int32_t>(1 + rRandom.Below(4));
        break;
      case 2:
        length = static_cast<int32_t>(1 + rRandom.Below(14));
        break;
      default:
        length = rRandom.Below(3) == 0 ? unit * 3 : unit;
        break;
      }
      for (int32_t i = 0; i < length && position < size; ++i, ++position)
      {
        if (dark)
        {
          line |= QR::QrSymbol::Bit(position);
        }
      }
      dark = !dark;
    }
    return line;
  }

  //! A symbol drawn from structured rows or columns, scored by both.
  bool CompareDrawnSymbol(Random& rRandom)
  {
    const int32_t size = (rRandom.Next() & 1u) != 0u ? 41 : 25;
    QR::QrSymbol symbol;
    symbol.Size = size;
    ReferenceSymbol reference{};
    QrcodegenReference_Clear(reference.data(), size);
    const bool asColumns = (rRandom.Next() & 1u) != 0u;
    const bool randomLines = rRandom.Below(4) == 0;
    // One in eight: a dark share right on a step of the balance rule (a multiple of 5 % of the modules), which random symbols never hit
    const bool onBalanceStep = rRandom.Below(8) == 0;
    if (onBalanceStep)
    {
      const int32_t total = size * size;
      const auto wanted = static_cast<int32_t>(rRandom.Below(21)) * total / 20;
      int32_t dark = 0;
      while (dark < wanted)
      {
        const auto x = static_cast<int32_t>(rRandom.Below(static_cast<uint64_t>(size)));
        const auto y = static_cast<int32_t>(rRandom.Below(static_cast<uint64_t>(size)));
        if (!symbol.IsDark(x, y))
        {
          symbol.SetDark(x, y);
          QrcodegenReference_SetModule(reference.data(), x, y, true);
          ++dark;
        }
      }
    }
    for (int32_t i = 0; !onBalanceStep && i < size; ++i)
    {
      const uint64_t line = randomLines ? rRandom.Next() & rRandom.Next() : StructuredLine(rRandom, size);
      for (int32_t k = 0; k < size; ++k)
      {
        if ((line & QR::QrSymbol::Bit(k)) != 0u)
        {
          const int32_t x = asColumns ? i : k;
          const int32_t y = asColumns ? k : i;
          symbol.SetDark(x, y);
          QrcodegenReference_SetModule(reference.data(), x, y, true);
        }
      }
    }
    const long expected = QrcodegenReference_PenaltyScore(reference.data());
    const int32_t actual = QR::PenaltyScore(symbol);
    if (actual != expected)
    {
      std::string rows;
      for (int32_t y = 0; y < size; ++y)
      {
        rows += std::to_string(symbol.Rows[static_cast<std::size_t>(y)] >> static_cast<uint32_t>(64 - size)) + " ";
      }
      Fail("a drawn symbol of " + std::to_string(size) + " modules: penalty " + std::to_string(actual) + ", the reference's is " +
           std::to_string(expected) + "; rows " + rows);
    }
    return actual == expected;
  }

  //! One thread's share of the run.
  void Work(const Settings& settings, const uint32_t thread)
  {
    Random random(settings.Seed + (uint64_t{thread} * 0xD1B54A32D192ED03u));
    const uint64_t payloads = settings.Payloads / settings.Threads;
    std::array<std::vector<uint8_t>, 2> previous;
    uint64_t frameIndex = random.Next() >> 20u;
    for (uint64_t i = 0; i < payloads && !g_failed; ++i)
    {
      const std::size_t which = i & 1u;
      const int32_t version = which == 0 ? 6 : 2;
      NextPayload(random, version, previous[which]);
      if (!ComparePayload(previous[which], version, (i & 7u) == 0u) || !CompareMarker(random, frameIndex))
      {
        return;
      }
      // Now and then data that does not fit: both refuse it
      if ((i & 1023u) == 0u)
      {
        const std::vector<uint8_t> tooLong(CapacityOf(version) + 1u + random.Below(4), static_cast<uint8_t>(random.Next()));
        if (!ComparePayload(tooLong, version, false))
        {
          return;
        }
      }
    }

    const uint64_t symbols = settings.Symbols / settings.Threads;
    for (uint64_t i = 0; i < symbols && !g_failed; ++i)
    {
      if (!CompareDrawnSymbol(random))
      {
        return;
      }
    }

    const uint64_t lines = settings.Lines / settings.Threads;
    for (uint64_t i = 0; i < lines && !g_failed; ++i)
    {
      const int32_t size = (i & 3u) == 0u ? 25 : 41;
      const uint64_t lineBits = ~uint64_t{0} << static_cast<uint32_t>(64 - size);
      const uint64_t line = (i & 1u) == 0u ? StructuredLine(random, size) : (random.Next() & lineBits);
      if (!CompareLine(line, size))
      {
        return;
      }
    }

    // Every line of 25 modules there is, this thread's share of them
    if (settings.Exhaustive)
    {
      for (uint64_t bits = thread; bits < (uint64_t{1} << 25u) && !g_failed; bits += settings.Threads)
      {
        if (!CompareLine(bits << 39u, 25))
        {
          return;
        }
      }
    }
  }

  bool ParseArguments(const int argc, const char* const* const argv, Settings& rSettings)
  {
    for (int i = 1; i < argc; ++i)
    {
      const std::string_view argument = argv[i];
      const auto value = [&]() { return i + 1 < argc ? std::strtoull(argv[++i], nullptr, 0) : 0u; };
      if (argument == "--payloads")
      {
        rSettings.Payloads = value();
      }
      else if (argument == "--lines")
      {
        rSettings.Lines = value();
      }
      else if (argument == "--symbols")
      {
        rSettings.Symbols = value();
      }
      else if (argument == "--threads")
      {
        rSettings.Threads = std::max<uint32_t>(1u, static_cast<uint32_t>(value()));
      }
      else if (argument == "--seed")
      {
        rSettings.Seed = value();
      }
      else if (argument == "--no-exhaustive")
      {
        rSettings.Exhaustive = false;
      }
      else
      {
        std::fprintf(stderr, "usage: %s [--payloads N] [--lines N] [--symbols N] [--threads N] [--seed N] [--no-exhaustive]\n", argv[0]);
        return false;
      }
    }
    return true;
  }
}

// Every exception is caught below; clang-tidy still follows MSVC's standard library into allocation failures past the handlers
// NOLINTNEXTLINE(bugprone-exception-escape)
int main(const int argc, const char* const* const argv)
{
  try
  {
    Settings settings;
    if (!ParseArguments(argc, argv, settings))
    {
      return 2;
    }
    std::printf("QR encoder stress: %llu payloads (each also as a marker), %llu drawn symbols, %llu lines%s, %u threads, seed %llu\n",
                static_cast<unsigned long long>(settings.Payloads), static_cast<unsigned long long>(settings.Symbols),
                static_cast<unsigned long long>(settings.Lines), settings.Exhaustive ? ", every line of 25 modules" : "", settings.Threads,
                static_cast<unsigned long long>(settings.Seed));
    std::fflush(stdout);
    const auto start = std::chrono::steady_clock::now();
    std::vector<std::thread> threads;
    threads.reserve(settings.Threads);
    for (uint32_t thread = 0; thread < settings.Threads; ++thread)
    {
      threads.emplace_back(Work, std::cref(settings), thread);
    }
    for (std::thread& rThread : threads)
    {
      rThread.join();
    }
    const double seconds = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    if (g_failed)
    {
      std::printf("FAILED after %.1f s\n", seconds);
      return 1;
    }
    std::printf("OK: the encoder and the reference agree on everything (%.1f s)\n", seconds);
    return 0;
  }
  catch (const std::exception& ex)
  {
    std::fprintf(stderr, "error: %s\n", ex.what());
    return 2;
  }
}
