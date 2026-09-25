//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Solves small dense linear systems (Gaussian elimination with partial pivoting) for the homography fit and refinement.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal static class LinearSolver
  {
    /// <summary>
    /// Solve A x = b where <paramref name="augmented"/> holds the n x (n+1) matrix [A | b] row by row. The matrix is destroyed. Returns false
    /// when A is (nearly) singular.
    /// </summary>
    public static bool Solve(Span<double> augmented, int n, Span<double> result)
    {
      int w = n + 1;
      for (int column = 0; column < n; ++column)
      {
        int pivot = column;
        for (int row = column + 1; row < n; ++row)
        {
          if (Math.Abs(augmented[(row * w) + column]) > Math.Abs(augmented[(pivot * w) + column]))
            pivot = row;
        }
        if (Math.Abs(augmented[(pivot * w) + column]) < 1e-10)
          return false;
        if (pivot != column)
        {
          for (int k = 0; k < w; ++k)
            (augmented[(column * w) + k], augmented[(pivot * w) + k]) = (augmented[(pivot * w) + k], augmented[(column * w) + k]);
        }
        for (int row = column + 1; row < n; ++row)
        {
          double factor = augmented[(row * w) + column] / augmented[(column * w) + column];
          for (int k = column; k < w; ++k)
            augmented[(row * w) + k] -= factor * augmented[(column * w) + k];
        }
      }
      for (int row = n - 1; row >= 0; --row)
      {
        double sum = augmented[(row * w) + n];
        for (int k = row + 1; k < n; ++k)
          sum -= augmented[(row * w) + k] * result[k];
        result[row] = sum / augmented[(row * w) + row];
      }
      return true;
    }
  }
}
