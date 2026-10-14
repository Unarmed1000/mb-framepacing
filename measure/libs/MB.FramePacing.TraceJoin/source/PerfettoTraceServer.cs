//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Serves one trace file to Perfetto's viewer, as Perfetto's own tools/open_trace_in_ui does: on 127.0.0.1 only, to a GET of its name, with
//* the header that lets ui.perfetto.dev read it, until it was sent once. A browser that asks first whether a public site may reach this
//* machine gets its answer. Nothing else is served.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. One trace file served once to ui.perfetto.dev from 127.0.0.1.</summary>
  public sealed class PerfettoTraceServer : IDisposable
  {
    // The most a request's line and headers may be: a browser's are a few hundred bytes
    private const int MaxRequestBytes = 16 * 1024;

    private readonly TcpListener m_listener;
    private readonly string m_path;
    private readonly string m_name;

    private PerfettoTraceServer(TcpListener listener, string path)
    {
      m_listener = listener;
      m_path = path;
      m_name = Path.GetFileName(path);
      Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The port it listens on.</summary>
    public int Port { get; }

    /// <summary>Where the viewer finds the trace.</summary>
    public string TraceUrl => PerfettoUi.TraceUrl(m_name, Port);

    /// <summary>
    /// Listen for the viewer on 127.0.0.1 at <paramref name="port"/> (the viewer's own by default; 0: any free one, for tests). A port that
    /// is taken is a <see cref="TraceException"/> that says what to do.
    /// </summary>
    public static PerfettoTraceServer Start(string path, int port = PerfettoUi.Port)
    {
      if (!File.Exists(path))
        throw new TraceException($"There is no trace at {path}.");
      var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
      try
      {
        listener.Start();
      }
      catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
      {
        listener.Dispose();
        throw new TraceException(
          FormattableString.Invariant($"Port {port} on 127.0.0.1 is taken, and it is the one ui.perfetto.dev loads a trace from this machine from. ")
            + "A trace processor started with --httpd (Perfetto's native acceleration) or another trace being opened holds it: close that and try again.",
          ex
        );
      }
      return new PerfettoTraceServer(listener, path);
    }

    /// <summary>
    /// Answer requests until the trace was sent once (true), <paramref name="timeout"/> passed (false) or <paramref name="cancellationToken"/>
    /// is cancelled. Then nothing listens any more.
    /// </summary>
    public async Task<bool> ServeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
      using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      stop.CancelAfter(timeout);
      try
      {
        while (true)
        {
          using var client = await m_listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
          if (await AnswerAsync(client, stop.Token).ConfigureAwait(false))
            return true;
        }
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        return false;
      }
      finally
      {
        m_listener.Stop();
      }
    }

    public void Dispose() => m_listener.Dispose();

    /// <summary>One request: true when it was the trace's and the trace was sent.</summary>
    private async Task<bool> AnswerAsync(TcpClient client, CancellationToken cancellationToken)
    {
      var stream = client.GetStream();
      string? request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
      if (request == null)
        return false;
      string[] parts = request.Split(' ', 3);
      string method = parts.Length > 0 ? parts[0] : string.Empty;
      string target = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
      bool ours = target == "/" + m_name;
      if (method == "OPTIONS" && ours)
      {
        // A browser's question whether the public site may read from this machine
        await WriteHeadAsync(
            stream,
            "204 No Content",
            0,
            "Access-Control-Allow-Methods: GET\r\nAccess-Control-Allow-Private-Network: true\r\n",
            cancellationToken
          )
          .ConfigureAwait(false);
        return false;
      }
      if (method != "GET" || !ours)
      {
        await WriteHeadAsync(stream, "404 Not Found", 0, string.Empty, cancellationToken).ConfigureAwait(false);
        return false;
      }
      using var file = new FileStream(m_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
      await WriteHeadAsync(stream, "200 OK", file.Length, "Content-Type: application/octet-stream\r\n", cancellationToken).ConfigureAwait(false);
      await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
      await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
      return true;
    }

    /// <summary>A request's line and headers, its first line returned; null when it ends or grows too long first.</summary>
    private static async Task<string?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
      var buffer = new byte[MaxRequestBytes];
      int length = 0;
      while (length < buffer.Length)
      {
        int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
          return null;
        length += read;
        string text = Encoding.ASCII.GetString(buffer, 0, length);
        int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end >= 0)
          return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
      }
      return null;
    }

    private static async Task WriteHeadAsync(NetworkStream stream, string status, long length, string headers, CancellationToken cancellationToken)
    {
      string head =
        $"HTTP/1.1 {status}\r\n"
        + $"Access-Control-Allow-Origin: {PerfettoUi.Origin}\r\n"
        + "Cache-Control: no-cache\r\n"
        + headers
        + $"Content-Length: {length.ToString(CultureInfo.InvariantCulture)}\r\n"
        + "Connection: close\r\n\r\n";
      await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
    }
  }
}
