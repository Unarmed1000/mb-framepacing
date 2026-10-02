//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Runs inside a real Unity editor (batch mode) on a throw-away project created by sdk/unity/check_in_unity.py:
//*   - the package compiles in Unity (the C# version and API level Unity uses),
//*   - the marker module produces the C++ module matrices (test-data/markers/modules.csv) on Unity's scripting runtime,
//*   - every drawing method renders pixel exact: FrameMarkerGL (the overlay's geometry), FrameMarkerMesh (the static grid with per-frame
//*     indices, through a command buffer), FrameMarkerTexture (the module bitmap scaled up) and FrameMarkerQuad (the dedicated shader, with
//*     GL and through a command buffer). The pixels read back from a render texture must equal the marker's quads, including the y flip,
//*     for frame, start, end and sync markers at several module sizes and odd origins;
//*   - FrameMarkerTexture holds the module-resolution bitmap, bottom row first as Unity textures are, and keeps a quiet zone outside its
//*     range within it;
//*   - FrameMarkerOverlay keeps drawing after a frame's draw threw (a provider of the game's that throws).
//* Exits the editor with 0 when everything passed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using MB.FramePacing;
using MB.FramePacing.Marker;
using MB.FramePacing.Marker.Unity;
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
      failures += CheckModuleDigest(Environment.GetEnvironmentVariable("MB_FRAMEPACING_TEST_DATA"));
      foreach (
        var method in new[]
        {
          "FrameMarkerGL",
          "FrameMarkerMesh",
          "FrameMarkerTexture",
          "FrameMarkerQuad GL",
          "FrameMarkerQuad mesh",
          "FrameMarkerQuad packed GL",
          "FrameMarkerQuad packed mesh",
        }
      )
        failures += CheckRendering(method);
      failures += CheckTexture();
      failures += CheckTextureQuietZone();
      failures += CheckOverlaySurvivesAnException();
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
    var bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    int mismatches = 0;
    for (int i = 1; i < lines.Length; ++i)
    {
      var f = lines[i].Split(',');
      var payload = new Payload(
        (MarkerKind)byte.Parse(f[0], CultureInfo.InvariantCulture),
        uint.Parse(f[1], CultureInfo.InvariantCulture),
        ulong.Parse(f[2], CultureInfo.InvariantCulture),
        (MarkerFlags)byte.Parse(f[3], CultureInfo.InvariantCulture),
        new TimeSpan(long.Parse(f[4], CultureInfo.InvariantCulture)),
        preferredFrameTime: new TimeSpan32(uint.Parse(f[5], CultureInfo.InvariantCulture)),
        targetFrameTime: new TimeSpan32(uint.Parse(f[6], CultureInfo.InvariantCulture)),
        intendedDisplayTime: new TickCount64(long.Parse(f[7], CultureInfo.InvariantCulture)),
        cpuStartTime: new TickCount64(long.Parse(f[8], CultureInfo.InvariantCulture)),
        cpuBusy: new TimeSpan32(uint.Parse(f[9], CultureInfo.InvariantCulture))
      );
      // Columns (the payload's in the order of the wire format): kind, runId, frameIndex, flags, animationTicks, preferredFrameTicks,
      // targetFrameTicks, intendedDisplayTicks, cpuStartTicks, cpuBusyTicks, startUtcTicks, sequenceIdHex (empty for other kinds), size,
      // modulesHex
      var sequenceId = f[11].Length > 0 ? SequenceId.FromBytes(FromHex(f[11])) : default;
      var start = new StartMetadata(long.Parse(f[10], CultureInfo.InvariantCulture), sequenceId);
      if (
        !generator.TryGenerateModules(payload, start, bits, out var matrix)
        || matrix.Size != int.Parse(f[12], CultureInfo.InvariantCulture)
        || Hex(matrix.Bits) != f[13]
      )
      {
        if (++mismatches <= 5)
          Debug.LogError($"FrameMarkerUnityCheck: module digest line {i + 1} differs ({payload})");
      }
    }
    Debug.Log($"FrameMarkerUnityCheck: module digest {lines.Length - 1 - mismatches}/{lines.Length - 1} rows match");
    return mismatches == 0 ? 0 : 1;
  }

  private static int CheckRendering(string method)
  {
    var cases = new[]
    {
      (
        Payload: new Payload(MarkerKind.Frame, 7, 4242, MarkerFlags.None, new TimeSpan(9_876_543)),
        Start: default(StartMetadata),
        Options: new Options(3, 4),
        Origin: new Point(17, 23)
      ),
      (
        new Payload(MarkerKind.SequenceStart, 7, 77, MarkerFlags.None, new TimeSpan(1_234)),
        new StartMetadata(638_000_000_000_000_000, new SequenceId(1, 2)),
        new Options(1, 0),
        new Point(33, 7)
      ),
      (new Payload(MarkerKind.SequenceEnd, 7, 99, MarkerFlags.None, new TimeSpan(5)), default(StartMetadata), new Options(2, 2), new Point(151, 41)),
      (new Payload(MarkerKind.Sync, 0, 4242, MarkerFlags.None, new TimeSpan(0)), default(StartMetadata), new Options(4, 4), new Point(5, 101)),
    };
    int failures = 0;
    var generator = new MarkerGenerator();
    var previous = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    var bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    var material = FrameMarkerGL.CreateMaterial();
    FrameMarkerMesh mesh = null;
    FrameMarkerTexture texture = null;
    FrameMarkerQuad quad = null;
    try
    {
      for (int c = 0; c < cases.Length; ++c)
      {
        var (payload, start, options, origin) = cases[c];
        if (!generator.TryGenerateModules(payload, start, bits, out var matrix))
          throw new InvalidOperationException("TryGenerateModules failed");
        var quads = new MarkerQuad[FrameMarker.MaxQuadCount];
        int quadCount = FrameMarker.ModulesToQuads(matrix, options, origin, quads);
        var target = BeginRender();
        {
          switch (method)
          {
            case "FrameMarkerGL":
              GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
              FrameMarkerGL.DrawQuads(material, quads.AsSpan(0, quadCount), Width, Height);
              break;
            case "FrameMarkerMesh":
            {
              // A first marker sets the static grid; the checked one reuses it and only changes the indices
              mesh ??= new FrameMarkerMesh();
              if (
                !generator.TryGenerateModules(
                  payload.WithKind(payload.Kind == MarkerKind.Sync ? MarkerKind.Sync : MarkerKind.Frame),
                  previous,
                  out var first
                )
                || !mesh.Update(first, options, origin, Height)
                || !mesh.Update(matrix, options, origin, Height)
              )
                throw new InvalidOperationException("FrameMarkerMesh.Update failed");
              DrawWithCommands(target, mesh.Mesh, material);
              break;
            }
            case "FrameMarkerTexture":
              texture ??= new FrameMarkerTexture();
              if (!texture.Update(matrix, options.QuietZoneModules))
                throw new InvalidOperationException("FrameMarkerTexture.Update failed");
              GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
              texture.DrawNow(options, origin, Width, Height);
              break;
            default:
              quad ??= new FrameMarkerQuad(packedBits: method.Contains("packed", StringComparison.Ordinal));
              if (!quad.Update(matrix, options, origin, Height))
                throw new InvalidOperationException("FrameMarkerQuad.Update failed (shader not available?)");
              if (method.EndsWith(" GL", StringComparison.Ordinal))
              {
                GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
                quad.DrawNow(Width);
              }
              else
              {
                DrawWithCommands(target, quad.Mesh, quad.Material);
              }
              break;
          }
        }
        var pixels = EndRender(target);
        failures += Compare($"{method} case {c} ({payload.Kind}, {options.ModuleSizePx} px)", quads, quadCount, pixels);
      }
    }
    finally
    {
      mesh?.Dispose();
      texture?.Dispose();
      quad?.Dispose();
      UnityEngine.Object.DestroyImmediate(material);
    }
    Debug.Log(
      $"FrameMarkerUnityCheck: {method} rendering {(failures == 0 ? "pixel exact" : $"{failures} cases differ")} ({SystemInfo.graphicsDeviceType})"
    );
    return failures == 0 ? 0 : 1;
  }

  private static void DrawWithCommands(RenderTexture target, Mesh mesh, Material material)
  {
    var commands = new CommandBuffer { name = "FrameMarkerUnityCheck" };
    commands.SetRenderTarget(target);
    commands.ClearRenderTarget(true, true, new Color(0.5f, 0.5f, 0.5f, 1f));
    commands.SetViewProjectionMatrices(Matrix4x4.identity, PixelSpace.Projection(Width, Height));
    commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
    Graphics.ExecuteCommandBuffer(commands);
    commands.Release();
  }

  /// <summary>A fresh render texture, made the active one to draw into.</summary>
  private static RenderTexture BeginRender()
  {
    var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
    target.Create();
    RenderTexture.active = target;
    return target;
  }

  /// <summary>The render texture's pixels (rows bottom up, as Unity textures are); releases it.</summary>
  private static Color32[] EndRender(RenderTexture target)
  {
    var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
    try
    {
      RenderTexture.active = target;
      readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      readback.Apply();
      return readback.GetPixels32();
    }
    finally
    {
      RenderTexture.active = null;
      target.Release();
      UnityEngine.Object.DestroyImmediate(target);
      UnityEngine.Object.DestroyImmediate(readback);
    }
  }

  /// <summary>The pixels must equal the quads painted in order, pixel (x, y) covered when Left &lt;= x &lt; Right and Top &lt;= y &lt; Bottom.</summary>
  private static int Compare(string name, MarkerQuad[] quads, int quadCount, Color32[] pixels)
  {
    var expected = new int[Width * Height];
    for (int i = 0; i < expected.Length; ++i)
      expected[i] = -1;
    for (int q = 0; q < quadCount; ++q)
    {
      for (int y = Math.Max(quads[q].Rect.Top, 0); y < Math.Min(quads[q].Rect.Bottom, Height); ++y)
      {
        for (int x = Math.Max(quads[q].Rect.Left, 0); x < Math.Min(quads[q].Rect.Right, Width); ++x)
          expected[(y * Width) + x] = quads[q].Dark ? 0 : 255;
      }
    }
    int differences = 0;
    for (int y = 0; y < Height; ++y)
    {
      for (int x = 0; x < Width; ++x)
      {
        int want = expected[(y * Width) + x];
        // Texture rows start at the bottom
        int got = pixels[((Height - 1 - y) * Width) + x].r;
        bool ok = want < 0 ? got > 0 && got < 255 : got == want;
        if (!ok && ++differences <= 3)
          Debug.LogError(
            $"FrameMarkerUnityCheck: {name} pixel ({x}, {y}) is {got}, expected {(want < 0 ? "background" : want.ToString(CultureInfo.InvariantCulture))}"
          );
      }
    }
    return differences == 0 ? 0 : 1;
  }

  private static int CheckTexture()
  {
    var bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    var texture = new FrameMarkerTexture();
    int differences = 0;
    try
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.Sync })
      {
        if (
          !new MarkerGenerator().TryGenerateModules(new Payload(kind, 5, 99, MarkerFlags.None, new TimeSpan(1234)), bits, out var matrix)
          || !texture.Update(matrix, 4)
        )
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

  /// <summary>A quiet zone outside its range is kept within it, as Options does: never a texture of another size than the marker's.</summary>
  private static int CheckTextureQuietZone()
  {
    var bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    var texture = new FrameMarkerTexture();
    int failures = 0;
    try
    {
      if (!new MarkerGenerator().TryGenerateModules(new Payload(MarkerKind.Sync, 5, 99, MarkerFlags.None, new TimeSpan(1234)), bits, out var matrix))
        throw new InvalidOperationException("TryGenerateModules failed");
      foreach (var (quietZone, expected) in new[] { (-30, 0), (-1, 0), (0, 0), (Options.MaxQuietZoneModules, 16), (20, 16), (1000, 16) })
      {
        int size = matrix.Size + (2 * expected);
        if (!texture.Update(matrix, quietZone) || texture.Texture.width != size || texture.Texture.height != size)
        {
          Debug.LogError(
            $"FrameMarkerUnityCheck: FrameMarkerTexture with a quiet zone of {quietZone} is {texture.Texture?.width}x{texture.Texture?.height}, expected {size}x{size}"
          );
          ++failures;
          continue;
        }
        // The symbol's first module is the quiet zone in from the top-left corner (texture rows start at the bottom)
        var pixels = texture.Texture.GetPixels32();
        var corner = pixels[((size - 1 - expected) * size) + expected];
        if (corner.r != (matrix.IsDark(0, 0) ? 0 : 255))
        {
          Debug.LogError(
            $"FrameMarkerUnityCheck: FrameMarkerTexture with a quiet zone of {quietZone}: the symbol is not at ({expected}, {expected})"
          );
          ++failures;
        }
      }
    }
    finally
    {
      texture.Dispose();
    }
    Debug.Log($"FrameMarkerUnityCheck: FrameMarkerTexture quiet zone {(failures == 0 ? "within its range" : $"{failures} sizes wrong")}");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// A frame whose draw throws (here: the game's animation clock) must not be the overlay's last: the next frames are drawn, and the run's
  /// phases go on. The overlay's end-of-frame loop is stepped by hand, since the editor's batch mode runs no coroutines.
  /// </summary>
  private static int CheckOverlaySurvivesAnException()
  {
    var gameObject = new GameObject("MB Frame Marker Overlay Check") { hideFlags = HideFlags.HideAndDontSave };
    try
    {
      var overlay = gameObject.AddComponent<FrameMarkerOverlay>();
      var loop = typeof(FrameMarkerOverlay).GetMethod("DrawAtEndOfFrame", BindingFlags.Instance | BindingFlags.NonPublic);
      if (loop == null)
      {
        Debug.LogError("FrameMarkerUnityCheck: FrameMarkerOverlay has no DrawAtEndOfFrame to step (the check follows the overlay's loop)");
        return 1;
      }
      int calls = 0;
      overlay.AnimationTimeProvider = () =>
      {
        ++calls;
        throw new InvalidOperationException("FrameMarkerUnityCheck: the animation clock throws (expected: the overlay reports it once)");
      };
      overlay.BeginRun();
      var frames = (IEnumerator)loop.Invoke(overlay, null);
      const int Frames = 4;
      // The first step waits for the end of a frame; each further one draws a frame
      for (int frame = 0; frame <= Frames; ++frame)
      {
        if (!frames.MoveNext())
        {
          Debug.LogError($"FrameMarkerUnityCheck: FrameMarkerOverlay's loop ended after {frame} frames");
          return 1;
        }
      }
      if (calls != Frames)
      {
        Debug.LogError($"FrameMarkerUnityCheck: FrameMarkerOverlay drew {calls} of {Frames} frames after its draw threw");
        return 1;
      }
      Debug.Log("FrameMarkerUnityCheck: FrameMarkerOverlay keeps drawing after an exception");
      return 0;
    }
    catch (Exception ex)
    {
      Debug.LogError($"FrameMarkerUnityCheck: FrameMarkerOverlay stopped at an exception in its draw: {(ex.InnerException ?? ex).GetType().Name}");
      return 1;
    }
    finally
    {
      UnityEngine.Object.DestroyImmediate(gameObject);
    }
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
