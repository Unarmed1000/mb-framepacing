//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Runs inside a real Unity editor (batch mode) on a throw-away project created by marker/unity/check_in_unity.py:
//*   - the package compiles in Unity (the C# version and API level Unity uses),
//*   - the core library produces the C++ module matrices (test-data/markers/modules.csv) on Unity's scripting runtime,
//*   - FrameMarkerGL (the overlay's drawing) and FrameMarkerMesh (command buffer drawing) render pixel exact: the pixels read back from a
//*     render texture must equal the marker's quads, including the y flip;
//*   - FrameMarkerTexture holds the module-resolution bitmap, bottom row first as Unity textures are.
//* Exits the editor with 0 when everything passed.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Text;
using MB.FrameMarker;
using MB.FrameMarker.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FrameMarkerUnityCheck
{
  private const int Width = 320;
  private const int Height = 240;

  public static void Run()
  {
    int failures = 0;
    try
    {
      failures += CheckModuleDigest(Environment.GetEnvironmentVariable("MB_FRAMEMARKER_TEST_DATA"));
      failures += CheckRendering(useMesh: false);
      failures += CheckRendering(useMesh: true);
      failures += CheckTexture();
    }
    catch (Exception ex)
    {
      Debug.LogException(ex);
      ++failures;
    }
    Debug.Log($"FrameMarkerUnityCheck: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    EditorApplication.Exit(failures == 0 ? 0 : 1);
  }

  private static int CheckModuleDigest(string testData)
  {
    var lines = File.ReadAllLines(Path.Combine(testData, "modules.csv"));
    var generator = new MarkerGenerator();
    var bits = new byte[Marker.MaxPackedModuleByteCount];
    int mismatches = 0;
    for (int i = 1; i < lines.Length; ++i)
    {
      var f = lines[i].Split(',');
      var payload = new Payload(
        ulong.Parse(f[2], CultureInfo.InvariantCulture),
        long.Parse(f[3], CultureInfo.InvariantCulture),
        uint.Parse(f[1], CultureInfo.InvariantCulture),
        (MarkerKind)byte.Parse(f[0], CultureInfo.InvariantCulture),
        long.Parse(f[4], CultureInfo.InvariantCulture),
        uint.Parse(f[5], CultureInfo.InvariantCulture),
        long.Parse(f[6], CultureInfo.InvariantCulture),
        uint.Parse(f[7], CultureInfo.InvariantCulture)
      );
      // Columns: kind, runId, frameIndex, animationTicks, intendedDisplayTicks, targetFrameTicks, cpuStartTicks, cpuBusyTicks,
      // startUtcTicks, sequenceIdHex (empty for other kinds), size, modulesHex
      var sequenceId = f[9].Length > 0 ? SequenceId.FromBytes(FromHex(f[9])) : default;
      var start = new StartMetadata(long.Parse(f[8], CultureInfo.InvariantCulture), sequenceId);
      if (
        !generator.TryGenerateModules(payload, start, bits, out var matrix)
        || matrix.Size != int.Parse(f[10], CultureInfo.InvariantCulture)
        || Hex(matrix.Bits) != f[11]
      )
      {
        if (++mismatches <= 5)
          Debug.LogError($"FrameMarkerUnityCheck: module digest line {i + 1} differs ({payload})");
      }
    }
    Debug.Log($"FrameMarkerUnityCheck: module digest {lines.Length - 1 - mismatches}/{lines.Length - 1} rows match");
    return mismatches == 0 ? 0 : 1;
  }

  private static int CheckRendering(bool useMesh)
  {
    string name = useMesh ? "FrameMarkerMesh" : "FrameMarkerGL";
    var payload = new Payload(4242, 9_876_543, 7);
    var options = new Options(3, 4);
    var origin = new Point(17, 23);
    var quads = new Quad[Marker.MaxQuadCount];
    var bits = new byte[Marker.MaxPackedModuleByteCount];
    if (!new MarkerGenerator().TryGenerateModules(payload, bits, out var matrix))
      throw new InvalidOperationException("TryGenerateModules failed");
    int quadCount = Marker.ModulesToQuads(matrix, options, origin, quads);

    var material = FrameMarkerGL.CreateMaterial();
    var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
    target.Create();
    var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
    var previous = RenderTexture.active;
    FrameMarkerMesh mesh = null;
    try
    {
      if (useMesh)
      {
        // A first marker sets the static grid; the checked one reuses it and only changes the indices
        mesh = new FrameMarkerMesh();
        var first = new byte[Marker.MaxPackedModuleByteCount];
        if (
          !new MarkerGenerator().TryGenerateModules(new Payload(1, 2, 3), first, out var firstMatrix)
          || !mesh.Update(firstMatrix, options, origin, Height)
        )
          throw new InvalidOperationException("FrameMarkerMesh.Update failed");
        if (!mesh.Update(matrix, options, origin, Height))
          throw new InvalidOperationException("FrameMarkerMesh.Update failed");
        var commands = new CommandBuffer { name = "FrameMarkerUnityCheck" };
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
        commands.SetViewProjectionMatrices(Matrix4x4.identity, PixelSpace.Projection(Width, Height));
        commands.DrawMesh(mesh.Mesh, Matrix4x4.identity, material, 0, 0);
        Graphics.ExecuteCommandBuffer(commands);
        commands.Release();
        RenderTexture.active = target;
      }
      else
      {
        RenderTexture.active = target;
        GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
        FrameMarkerGL.DrawQuads(material, quads.AsSpan(0, quadCount), Width, Height);
      }
      readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      readback.Apply();
    }
    finally
    {
      RenderTexture.active = previous;
      mesh?.Dispose();
      target.Release();
      UnityEngine.Object.DestroyImmediate(target);
      UnityEngine.Object.DestroyImmediate(material);
    }

    // Expected: the quads painted in order, pixel (x, y) covered when Left <= x < Right and Top <= y < Bottom (top-left origin)
    var expected = new int[Width * Height];
    for (int i = 0; i < expected.Length; ++i)
      expected[i] = -1;
    for (int q = 0; q < quadCount; ++q)
    {
      for (int y = quads[q].Top; y < quads[q].Bottom; ++y)
      {
        for (int x = quads[q].Left; x < quads[q].Right; ++x)
          expected[(y * Width) + x] = quads[q].Dark ? 0 : 255;
      }
    }

    int differences = 0;
    var pixels = readback.GetPixels32();
    for (int y = 0; y < Height; ++y)
    {
      for (int x = 0; x < Width; ++x)
      {
        int want = expected[(y * Width) + x];
        // Texture rows start at the bottom
        int got = pixels[((Height - 1 - y) * Width) + x].r;
        bool ok = want < 0 ? got > 0 && got < 255 : got == want;
        if (!ok && ++differences <= 5)
          Debug.LogError(
            $"FrameMarkerUnityCheck: {name} pixel ({x}, {y}) is {got}, expected {(want < 0 ? "background" : want.ToString(CultureInfo.InvariantCulture))}"
          );
      }
    }
    UnityEngine.Object.DestroyImmediate(readback);
    Debug.Log(
      $"FrameMarkerUnityCheck: {name} rendering {(differences == 0 ? "pixel exact" : $"{differences} pixels differ")} ({SystemInfo.graphicsDeviceType})"
    );
    return differences == 0 ? 0 : 1;
  }

  private static int CheckTexture()
  {
    var bits = new byte[Marker.MaxPackedModuleByteCount];
    var texture = new FrameMarkerTexture();
    int differences = 0;
    try
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.Sync })
      {
        if (!new MarkerGenerator().TryGenerateModules(new Payload(99, 1234, 5, kind), bits, out var matrix) || !texture.Update(matrix, 4))
          throw new InvalidOperationException("FrameMarkerTexture.Update failed");
        int size = matrix.Size + 8;
        var pixels = texture.Texture.GetPixels32();
        for (int y = 0; y < size; ++y)
        {
          for (int x = 0; x < size; ++x)
          {
            bool inSymbol = x >= 4 && y >= 4 && x < size - 4 && y < size - 4;
            int want = inSymbol && matrix.IsDark(x - 4, y - 4) ? 0 : 255;
            // Texture rows start at the bottom
            var got = pixels[((size - 1 - y) * size) + x];
            if ((got.r != want || got.g != want || got.b != want || got.a != 255) && ++differences <= 5)
              Debug.LogError($"FrameMarkerUnityCheck: FrameMarkerTexture {kind} texel ({x}, {y}) is {got}, expected {want}");
          }
        }
      }
    }
    finally
    {
      texture.Dispose();
    }
    Debug.Log($"FrameMarkerUnityCheck: FrameMarkerTexture {(differences == 0 ? "exact" : $"{differences} texels differ")}");
    return differences == 0 ? 0 : 1;
  }

  private static string Hex(ReadOnlySpan<byte> bytes)
  {
    var hex = new StringBuilder(bytes.Length * 2);
    foreach (byte value in bytes)
      hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
    return hex.ToString();
  }

  private static byte[] FromHex(string hex)
  {
    var bytes = new byte[hex.Length / 2];
    for (int i = 0; i < bytes.Length; ++i)
      bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return bytes;
  }
}
