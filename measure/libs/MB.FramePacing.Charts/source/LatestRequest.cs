//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Runs work on the thread pool where only the latest request counts: a new request cancels the one before, and a request that a newer one
//* overtook returns null instead of its result. The GUI builds its cards with it, so a zoom never shows a card older than the last one asked
//* for. The result is checked on the caller's context (the GUI's thread), where the caller also applies it.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Threading;
using System.Threading.Tasks;

namespace MB.FramePacing.Charts
{
  public sealed class LatestRequest<T>
    where T : class
  {
    private readonly object m_lock = new object();
    private CancellationTokenSource? m_current;
    private long m_generation;

    /// <summary>
    /// Run <paramref name="work"/> on the thread pool: its result, or null when a newer request (or <see cref="Cancel"/>) came in meanwhile.
    /// The work gets a token that is cancelled then; it may stop early by throwing <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<T?> Run(Func<CancellationToken, T> work)
    {
      CancellationTokenSource source;
      long generation;
      lock (m_lock)
      {
        m_current?.Cancel();
        m_current = source = new CancellationTokenSource();
        generation = ++m_generation;
      }
      try
      {
        // Back on the caller's context before the check: nothing can overtake between the check and the caller applying the result there
        var result = await Task.Run(() => work(source.Token), source.Token);
        lock (m_lock)
          return generation == m_generation ? result : null;
      }
      catch (OperationCanceledException) when (source.IsCancellationRequested)
      {
        return null;
      }
      finally
      {
        lock (m_lock)
        {
          if (ReferenceEquals(m_current, source))
            m_current = null;
        }
        source.Dispose();
      }
    }

    /// <summary>Drop the request running now: it returns null.</summary>
    public void Cancel()
    {
      lock (m_lock)
      {
        m_current?.Cancel();
        m_current = null;
        ++m_generation;
      }
    }
  }
}
