//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* BenchmarkDotNet entry point:
//*   dotnet run -c Release --project sdk/csharp/marker/Benchmarks/MB.FramePacing.Marker.Benchmarks.csproj -- --filter "*"
//* (name the csproj: building the folder picks its .slnx, which does not build the library optimized).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using BenchmarkDotNet.Running;

namespace MB.FramePacing.Marker.Benchmarks
{
  internal static class Program
  {
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
  }
}
