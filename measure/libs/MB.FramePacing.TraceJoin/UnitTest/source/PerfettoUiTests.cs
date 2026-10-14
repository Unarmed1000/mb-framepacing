//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Opening a trace in ui.perfetto.dev: the link, and the server that hands the trace over once (on a free port here, never the viewer's own).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class PerfettoUiTests
  {
    [Test]
    public void TheLink_LoadsTheTraceFromThisMachine_AtTheRunsStretch_WithTheCapturesTracksPinned()
    {
      string url = PerfettoUi.TraceUrl("run-1-augmented.pftrace");
      Assert.That(url, Is.EqualTo("http://127.0.0.1:9001/run-1-augmented.pftrace"));

      string link = PerfettoUi.Link(url, (1_000, 2_000));

      Assert.That(
        link,
        Is.EqualTo(
          "https://ui.perfetto.dev/#!/?url=http%3A%2F%2F127.0.0.1%3A9001%2Frun-1-augmented.pftrace&visStart=1000&visEnd=2000&startupCommands="
            + Uri.EscapeDataString(PerfettoUi.StartupCommands())
        )
      );
      // No stretch, or one without length: the whole trace
      Assert.That(PerfettoUi.Link(url, null), Does.Not.Contain("visStart"));
      Assert.That(PerfettoUi.Link(url, (5, 5)), Does.Not.Contain("visStart"));
      // A name the address has to escape
      Assert.That(PerfettoUi.TraceUrl("a b#.pftrace", 1234), Is.EqualTo("http://127.0.0.1:1234/a%20b%23.pftrace"));
    }

    [Test]
    public void TheStartupCommands_ExpandTheCapturesTracks_AndPinTheFramesNextToTheApplicationsRecord()
    {
      using var commands = JsonDocument.Parse(PerfettoUi.StartupCommands());

      var list = commands.RootElement.EnumerateArray().ToList();
      Assert.That(
        list.Select(c => c.GetProperty("id").GetString()),
        Is.EqualTo(new[] { "dev.perfetto.ExpandTracksByRegex", "dev.perfetto.PinTracksByRegex" })
      );
      var expand = new System.Text.RegularExpressions.Regex(list[0].GetProperty("args")[0].GetString()!);
      Assert.That(expand.IsMatch(TraceTrackWriter.RootName("busy (60 Hz)")), Is.True);
      Assert.That(expand.IsMatch("mbfp frames"), Is.False);
      var pin = new System.Text.RegularExpressions.Regex(list[1].GetProperty("args")[0].GetString()!);
      foreach (string name in new[] { "Refreshes (captured)", "On screen (measured)", "CPU (from the markers)", "mbfp frames" })
        Assert.That(pin.IsMatch(name), Is.True, name);
      foreach (string name in new[] { "Frame events", "On screen (measured) 2", "Refreshes (captured" })
        Assert.That(pin.IsMatch(name), Is.False, name);
    }

    [Test]
    public async Task TheServer_SendsTheTraceOnce_ToItsName_WithTheHeaderTheViewerNeeds()
    {
      using var temp = new TempFolder();
      string path = temp.File("run-1-augmented.pftrace");
      byte[] bytes = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
      File.WriteAllBytes(path, bytes);
      using var server = PerfettoTraceServer.Start(path, port: 0);
      Assert.That(server.TraceUrl, Is.EqualTo($"http://127.0.0.1:{server.Port}/run-1-augmented.pftrace"));
      var served = server.ServeAsync(TimeSpan.FromSeconds(30));
      using var http = new HttpClient();

      // Anything else is not there; a browser's question first is answered and is not the trace
      using (var other = await http.GetAsync($"http://127.0.0.1:{server.Port}/captures.mbcd"))
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
      using (var asked = new HttpRequestMessage(HttpMethod.Options, server.TraceUrl))
      {
        asked.Headers.Add("Origin", PerfettoUi.Origin);
        asked.Headers.Add("Access-Control-Request-Private-Network", "true");
        using var answer = await http.SendAsync(asked);
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(answer.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { PerfettoUi.Origin }));
        Assert.That(answer.Headers.GetValues("Access-Control-Allow-Private-Network"), Is.EqualTo(new[] { "true" }));
      }
      Assert.That(served.IsCompleted, Is.False);

      using (var trace = await http.GetAsync(server.TraceUrl))
      {
        Assert.That(trace.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(trace.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { PerfettoUi.Origin }));
        Assert.That(await trace.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes));
      }
      Assert.That(await served, Is.True);
      // Once: then nothing listens
      Assert.That(async () => await http.GetAsync(server.TraceUrl), Throws.InstanceOf<HttpRequestException>());
    }

    [Test]
    public async Task TheServer_GivesUp_WhenNothingAsksInTime()
    {
      using var temp = new TempFolder();
      string path = temp.File("t.pftrace");
      File.WriteAllBytes(path, new byte[] { 1 });
      using var server = PerfettoTraceServer.Start(path, port: 0);

      Assert.That(await server.ServeAsync(TimeSpan.FromMilliseconds(200)), Is.False);
    }

    [Test]
    public void APortThatIsTaken_OrATraceThatIsNot_SaysWhatToDo()
    {
      using var temp = new TempFolder();
      string path = temp.File("t.pftrace");
      File.WriteAllBytes(path, new byte[] { 1 });
      var holder = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
      holder.Start();
      try
      {
        int port = ((IPEndPoint)holder.LocalEndpoint).Port;
        Assert.That(
          Assert.Throws<TraceException>(() => PerfettoTraceServer.Start(path, port))!.Message,
          Does.Contain($"Port {port} on 127.0.0.1 is taken").And.Contain("--httpd")
        );
      }
      finally
      {
        holder.Stop();
      }
      Assert.That(
        Assert.Throws<TraceException>(() => PerfettoTraceServer.Start(temp.File("none.pftrace"), 0))!.Message,
        Does.Contain("There is no trace")
      );
    }
  }
}
