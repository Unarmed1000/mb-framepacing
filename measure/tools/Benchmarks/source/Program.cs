//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* BenchmarkDotNet entry point: dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*" (name the csproj:
//* building the folder picks its .slnx, which does not build the libraries optimized).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using BenchmarkDotNet.Running;

namespace MB.FramePacing.Benchmarks
{
  internal static class Program
  {
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
  }
}
