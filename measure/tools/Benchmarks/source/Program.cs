//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* BenchmarkDotNet entry point: dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*" (name the csproj:
//* building the folder picks its .slnx, which does not build the libraries optimized). The long-running benchmarks (category LongRunning:
//* the cards and playback pages of hour-long runs) are left out unless --long-running is given.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;

namespace MB.FramePacing.Benchmarks
{
  internal static class Program
  {
    private const string LongRunningOption = "--long-running";

    private static void Main(string[] args)
    {
      IConfig config = DefaultConfig.Instance;
      if (!args.Contains(LongRunningOption))
      {
        config = config.AddFilter(new SimpleFilter(b => !b.Descriptor.Categories.Contains(BenchmarkCategories.LongRunning)));
        var skipped = typeof(Program)
          .Assembly.GetTypes()
          .Where(t => t.GetCustomAttribute<BenchmarkCategoryAttribute>()?.Categories.Contains(BenchmarkCategories.LongRunning) == true)
          .Select(t => t.Name)
          .Order(StringComparer.Ordinal);
        Console.WriteLine($"// Long-running benchmarks left out ({string.Join(", ", skipped)}): add {LongRunningOption} to run them.");
      }
      BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Where(a => a != LongRunningOption).ToArray(), config);
    }
  }
}
