//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Perfetto's trace processor with one trace loaded: the user's trace_processor_shell, started as a separate process that listens on
//* this machine only (the loopback address, a port that is free), and asked SQL over its HTTP interface, as Perfetto's own Python package
//* asks it. The trace is loaded once, however many queries follow. Dispose ends the process.
//*
//* Nothing is uploaded: the program reads the file from the disk and answers this process.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NLog;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A running trace processor with one trace loaded. Dispose it: it holds a process.</summary>
  public sealed class TraceProcessor : ITraceQueries, IDisposable
  {
    /// <summary>How long a trace may take to load before the start counts as failed.</summary>
    public static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromMinutes(10);

    private const int QuerySqlField = 1;
    private const int MaxLogLength = 4000;

    private static readonly Logger g_log = LogManager.GetCurrentClassLogger();

    private readonly Process m_process;
    private readonly HttpClient m_client;
    private readonly StringBuilder m_output = new StringBuilder();
    private readonly string m_traceName;
    private bool m_disposed;

    private TraceProcessor(Process process, HttpClient client, string traceName)
    {
      m_process = process;
      m_client = client;
      m_traceName = traceName;
    }

    /// <summary>
    /// Start <paramref name="processorPath"/> with <paramref name="tracePath"/> and wait until the trace is loaded. A program that does not
    /// start, ends, or does not answer within <paramref name="loadTimeout"/> is a <see cref="TraceException"/> with what it printed.
    /// </summary>
    public static TraceProcessor Start(
      string processorPath,
      string tracePath,
      TimeSpan? loadTimeout = null,
      CancellationToken cancellationToken = default
    )
    {
      string traceName = Path.GetFileName(tracePath);
      if (!File.Exists(tracePath))
        throw new TraceException($"{tracePath} is not a file.");
      int port = FreePort();
      var startInfo = new ProcessStartInfo(processorPath)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      // The classic flags, which every version takes: an HTTP server on the loopback address only
      foreach (
        var argument in new[]
        {
          "-D",
          "--http-port",
          port.ToString(CultureInfo.InvariantCulture),
          "--http-ip-address",
          IPAddress.Loopback.ToString(),
          Path.GetFullPath(tracePath),
        }
      )
        startInfo.ArgumentList.Add(argument);

      Process process;
      try
      {
        process = Process.Start(startInfo) ?? throw new InvalidOperationException("the process did not start");
      }
      catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
      {
        throw new TraceException($"Perfetto's trace processor could not be started ('{processorPath}'): {ex.Message}", ex);
      }

      var client = new HttpClient { BaseAddress = new Uri($"http://{IPAddress.Loopback}:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
      var processor = new TraceProcessor(process, client, traceName);
      try
      {
        process.StandardInput.Close();
        process.OutputDataReceived += processor.OnOutput;
        process.ErrorDataReceived += processor.OnOutput;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        processor.WaitUntilLoaded(loadTimeout ?? DefaultLoadTimeout, cancellationToken);
        g_log.Info("Trace processor ready on port {Port} with {Trace}", port, traceName);
        return processor;
      }
      catch
      {
        processor.Dispose();
        throw;
      }
    }

    public TraceQueryResult Query(string sql)
    {
      ObjectDisposedException.ThrowIf(m_disposed, this);
      var arguments = new ProtoWriter();
      arguments.Text(QuerySqlField, sql);
      try
      {
        using var content = new ByteArrayContent(arguments.Bytes.ToArray());
        using var request = new HttpRequestMessage(HttpMethod.Post, "query") { Content = content };
        using var response = m_client.Send(request, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        using var body = new MemoryStream();
        response.Content.ReadAsStream().CopyTo(body);
        g_log.Trace("Trace processor query: {Bytes} bytes for {Sql}", body.Length, sql);
        return TraceQueryResult.Decode(body.GetBuffer().AsSpan(0, checked((int)body.Length)), sql);
      }
      catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
      {
        throw new TraceException($"The trace processor did not answer a query about {m_traceName} ({ex.Message}).{Printed()}", ex);
      }
    }

    public void Dispose()
    {
      if (m_disposed)
        return;
      m_disposed = true;
      m_client.Dispose();
      try
      {
        if (!m_process.HasExited)
        {
          m_process.Kill(entireProcessTree: true);
          m_process.WaitForExit(5000);
        }
      }
      catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
      {
        g_log.Warn(ex, "The trace processor could not be ended");
      }
      m_process.Dispose();
    }

    /// <summary>A port of the loopback address that is free now.</summary>
    private static int FreePort()
    {
      var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      try
      {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
      }
      finally
      {
        listener.Stop();
      }
    }

    /// <summary>The server answers its status once the trace given on the command line is loaded.</summary>
    private void WaitUntilLoaded(TimeSpan timeout, CancellationToken cancellationToken)
    {
      var clock = Stopwatch.StartNew();
      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (m_process.HasExited)
          throw new TraceException($"Perfetto's trace processor ended while it read {m_traceName} (exit code {m_process.ExitCode}).{Printed()}");
        try
        {
          using var request = new HttpRequestMessage(HttpMethod.Get, "status");
          using var poll = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          using var response = m_client.Send(request, HttpCompletionOption.ResponseContentRead, poll.Token);
          if (response.IsSuccessStatusCode)
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or OperationCanceledException)
        {
          // Not listening yet: it is still reading the trace
        }
        if (clock.Elapsed > timeout)
          throw new TraceException(
            $"Perfetto's trace processor did not finish reading {m_traceName} within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s.{Printed()}"
          );
        Thread.Sleep(100);
      }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
      if (e.Data == null)
        return;
      lock (m_output)
      {
        // The newest of what it printed: enough for an error's text
        m_output.Append(e.Data).Append('\n');
        if (m_output.Length > MaxLogLength)
          m_output.Remove(0, m_output.Length - MaxLogLength);
      }
    }

    private string Printed()
    {
      lock (m_output)
      {
        string text = m_output.ToString().Trim();
        return text.Length > 0 ? " It printed: " + text : string.Empty;
      }
    }
  }
}
