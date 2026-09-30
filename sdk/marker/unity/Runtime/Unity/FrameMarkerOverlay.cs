//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Add this component to any GameObject and every frame shows the marker: drawn at the end of the frame (after post processing, upscaling
//* and UI) straight into the output, pixel exact, in pure black and white. The frame index is Time.frameCount and the animation time is
//* Time.timeAsDouble unless AnimationTimeProvider supplies the game's own clock; the CPU start time and CPU busy come from Unity's clock
//* unless the game supplies its pacer's. BeginRun / EndRun (or the RunFor coroutine) bracket the part to measure with the
//* start and end markers. Nothing is allocated per frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections;
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  [DisallowMultipleComponent]
  [AddComponentMenu("MB/Frame Marker Overlay")]
  public sealed class FrameMarkerOverlay : MonoBehaviour
  {
    // Android and iOS run at this rate while Application.targetFrameRate is unset
    private const double MobileDefaultFps = 30;

    [Header("Capture")]
    [Tooltip(
      "Height of the frames the capture tool stores, for example 540 for --scale 960x540. Picks the module size (3 stored pixels per module). 0 = the output height."
    )]
    [SerializeField]
    private int m_storedHeight = 540;

    [Tooltip("The capture card delivers MJPEG (4 stored pixels per module instead of 3).")]
    [SerializeField]
    private bool m_mjpeg;

    [Tooltip("Module size in output pixels. 0 = pick it from the stored height.")]
    [SerializeField]
    private int m_moduleSizePx;

    [Header("Placement")]
    [Tooltip("Also draw the small sync marker at the bottom left: the analysis detects tearing with it, and camera capture needs it for its timing.")]
    [SerializeField]
    private bool m_syncMarker;

    [Tooltip("Draw frame markers (run id 0) while no run is active.")]
    [SerializeField]
    private bool m_drawWhenIdle = true;

    [Header("Runs")]
    [Tooltip(
      "How long the start and end markers stay on screen. One captured frame is enough; about three frames of the slowest capture leave slack for a dropped or torn capture (0.1 s covers 30 fps)."
    )]
    [SerializeField]
    [Min(0f)]
    private float m_sequenceMarkerSeconds = 0.1f;

    [Tooltip("Optional: an unlit vertex color material without blending, depth test or culling. Empty = Hidden/Internal-Colored.")]
    [SerializeField]
    private Material m_material;

    [Tooltip(
      "How the marker is drawn, fastest first: Shader Packed Bits (one quad whose shader reads the 211 packed module bytes), Shader (one quad, a texel per module; both need shader model 3.5, else they draw Geometry), Bitmap (a module texture scaled up) or Geometry (pixel aligned quads, works everywhere). All draw the same pixels."
    )]
    [SerializeField]
    private FrameMarkerRenderMode m_renderMode = FrameMarkerRenderMode.ShaderPackedBits;

    [Tooltip(
      "The Shader Packed Bits mode's shader (Hidden/MB/FrameMarkerQuadPacked, set when the component is added): referencing it keeps it in player builds."
    )]
    [SerializeField]
    private Shader m_packedShader;

    [Tooltip("The Shader mode's shader (Hidden/MB/FrameMarkerQuad, set when the component is added): referencing it keeps it in player builds.")]
    [SerializeField]
    private Shader m_quadShader;

    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly byte[] m_modules = new byte[Marker.MaxPackedModuleByteCount];
    private readonly Quad[] m_quads = new Quad[Marker.MaxQuadCount];
    private FrameMarkerTexture m_mainTexture;
    private FrameMarkerTexture m_syncTexture;
    private FrameMarkerQuad m_mainQuad;
    private FrameMarkerQuad m_syncQuad;
    private bool m_shaderWarned;
    private WaitForEndOfFrame m_endOfFrame;
    private Coroutine m_drawing;
    private Material m_ownedMaterial;
    private StartMetadata m_start;
    private double m_phaseStartTime;
    private bool m_warned;

    /// <summary>How the marker is drawn (the same pixels every way).</summary>
    public FrameMarkerRenderMode RenderMode
    {
      get => m_renderMode;
      set => m_renderMode = value;
    }

    /// <summary>The game's animation clock in seconds. Null = Time.timeAsDouble.</summary>
    public Func<double> AnimationTimeProvider { get; set; }

    /// <summary>
    /// When the game's frame pacer intends the frame to become visible, in ticks (100 ns) on a steady clock, for example the desired present
    /// time a pacing plugin schedules. Null (or 0) = unknown: Unity does not expose it.
    /// </summary>
    public Func<long> IntendedDisplayTicksProvider { get; set; }

    /// <summary>
    /// The interval the game aims for between frames, in ticks (100 ns). Null = what Unity's settings aim for: on Android and iOS
    /// Application.targetFrameRate (30 fps when unset); elsewhere the refresh rate divided by QualitySettings.vSyncCount while vsync is on,
    /// else Application.targetFrameRate; 0 when unknown (XR platforms: give the XR display's rate here).
    /// </summary>
    public Func<uint> TargetFrameTicksProvider { get; set; }

    /// <summary>
    /// The interval the game wants to run at, in ticks (100 ns): what it would aim for if nothing held it back. It differs from the target
    /// frame time only while a pacer runs the game slower than it wants. <see cref="Marker.OnDemandFrameTicks"/> when the game presents only
    /// when something changes. Null = the same default as the target frame time (Unity's Application.targetFrameRate is the rate the game
    /// asks for, and Unity does not lower it on its own).
    /// </summary>
    public Func<uint> PreferredFrameTicksProvider { get; set; }

    /// <summary>
    /// True when nothing animates while this frame is on screen, until the next frame (no pending work after it: an idle screen, a paused
    /// menu): the marker's StaticAfter flag. Null = never.
    /// </summary>
    public Func<bool> StaticAfterProvider { get; set; }

    /// <summary>
    /// True when nothing animated while the previous frame was on screen, for a game that only knows it once it renders this frame: the
    /// marker's StaticBefore flag. Null = never.
    /// </summary>
    public Func<bool> StaticBeforeProvider { get; set; }

    /// <summary>
    /// CPU start time: when the CPU started working on the frame, in ticks (100 ns) on the same steady clock as
    /// <see cref="IntendedDisplayTicksProvider"/>. Null = Unity's unscaled time at the beginning of the frame while no
    /// IntendedDisplayTicksProvider is set (its clock is the game's own), otherwise 0 (unknown).
    /// </summary>
    public Func<long> CpuStartTicksProvider { get; set; }

    /// <summary>
    /// CPU busy: how long the CPU worked on the frame before presenting it, in ticks (100 ns). Null = Unity's real time when the marker
    /// is drawn (the end of the frame, just before Present) minus the time at the beginning of the frame.
    /// </summary>
    public Func<uint> CpuBusyTicksProvider { get; set; }

    /// <summary>
    /// Draw frame markers (run id 0) while no run is active (the Inspector's Draw When Idle). Turn it off to show markers only during
    /// runs, for example while a game is still in its menus. Runs always draw their markers.
    /// </summary>
    public bool DrawWhenIdle
    {
      get => m_drawWhenIdle;
      set => m_drawWhenIdle = value;
    }

    public MarkerPhase Phase { get; private set; } = MarkerPhase.Idle;

    /// <summary>The id of the current (or last) run. Every run gets the next id.</summary>
    public uint RunId { get; private set; }

    /// <summary>The sequence id of the current (or last) run, as its start marker carries it.</summary>
    public SequenceId SequenceId => m_start.SequenceId;

    /// <summary>Start a measured run with a new UUID as its sequence id.</summary>
    public void BeginRun() => BeginRun(SequenceId.FromGuid(Guid.NewGuid()));

    /// <summary>
    /// Start a measured run: the start marker (with the sequence id and the current time) is shown first, then the frame markers. The
    /// sequence id is any 16 bytes unique to this run: a UUID (<see cref="SequenceId.FromGuid"/>) or a short text tag
    /// (<see cref="SequenceId.TryFromText"/>).
    /// </summary>
    public void BeginRun(SequenceId sequenceId)
    {
      ++RunId;
      m_start = StartMetadata.Create(DateTime.UtcNow, sequenceId);
      EnterPhase(MarkerPhase.Start);
    }

    /// <summary>End the run: the end marker is shown, then the overlay goes back to idle.</summary>
    public void EndRun()
    {
      if (Phase == MarkerPhase.Start || Phase == MarkerPhase.Running)
        EnterPhase(MarkerPhase.End);
    }

    /// <summary>Measure <paramref name="seconds"/> (real time) as a run with a new UUID as its sequence id: start it from a coroutine.</summary>
    public IEnumerator RunFor(double seconds) => RunFor(SequenceId.FromGuid(Guid.NewGuid()), seconds);

    /// <summary>Measure <paramref name="seconds"/> (real time) as a run with <paramref name="sequenceId"/>: start it from a coroutine.</summary>
    public IEnumerator RunFor(SequenceId sequenceId, double seconds)
    {
      BeginRun(sequenceId);
      while (Phase == MarkerPhase.Start)
        yield return null;
      double end = Time.realtimeSinceStartupAsDouble + seconds;
      while (Time.realtimeSinceStartupAsDouble < end)
        yield return null;
      EndRun();
      while (Phase == MarkerPhase.End)
        yield return null;
    }

    private void OnEnable()
    {
      if (m_endOfFrame == null)
        m_endOfFrame = new WaitForEndOfFrame();
      m_drawing = StartCoroutine(DrawAtEndOfFrame());
    }

    private void OnDisable()
    {
      if (m_drawing != null)
        StopCoroutine(m_drawing);
      m_drawing = null;
    }

    private void OnDestroy()
    {
      if (m_ownedMaterial != null)
        Destroy(m_ownedMaterial);
      m_mainTexture?.Dispose();
      m_syncTexture?.Dispose();
      m_mainQuad?.Dispose();
      m_syncQuad?.Dispose();
    }

    private IEnumerator DrawAtEndOfFrame()
    {
      while (true)
      {
        yield return m_endOfFrame;
        Draw();
      }
    }

    private void Draw()
    {
      UpdatePhase();
      if (Phase == MarkerPhase.Idle && !m_drawWhenIdle)
        return;
      int width = Screen.width;
      int height = Screen.height;
      int storedHeight = m_storedHeight > 0 ? m_storedHeight : height;
      int moduleSize = m_moduleSizePx > 0 ? m_moduleSizePx : Marker.RecommendModuleSizePx(height, storedHeight, m_mjpeg);
      var options = new Options(moduleSize);
      // Integer downscales keep module edges on stored pixel edges when the origin is a multiple of the ratio
      int align = height % storedHeight == 0 ? height / storedHeight : 1;
      WarnOnce(moduleSize, height, storedHeight);

      var payload = new Payload(
        Kind(),
        Phase == MarkerPhase.Idle ? 0u : RunId,
        (ulong)Time.frameCount,
        (StaticAfterProvider != null && StaticAfterProvider() ? MarkerFlags.StaticAfter : MarkerFlags.None)
          | (StaticBeforeProvider != null && StaticBeforeProvider() ? MarkerFlags.StaticBefore : MarkerFlags.None),
        Marker.SecondsToTicks(AnimationTime()),
        preferredFrameTicks: PreferredFrameTicksProvider != null ? PreferredFrameTicksProvider() : DefaultTargetFrameTicks(),
        targetFrameTicks: TargetFrameTicksProvider != null ? TargetFrameTicksProvider() : DefaultTargetFrameTicks(),
        intendedDisplayTicks: IntendedDisplayTicksProvider != null ? IntendedDisplayTicksProvider() : 0,
        cpuStartTicks: CpuStartTicks(),
        cpuBusyTicks: CpuBusyTicksProvider != null ? CpuBusyTicksProvider() : UnityCpuBusyTicks()
      );

      DrawMarker(payload, options, Marker.RecommendedOrigin(payload.Kind, width, height, options, align), width, height, sync: false);
      if (m_syncMarker)
      {
        var sync = payload.WithKind(MarkerKind.Sync);
        DrawMarker(sync, options, Marker.RecommendedOrigin(MarkerKind.Sync, width, height, options, align), width, height, sync: true);
      }
    }

    private void DrawMarker(in Payload payload, in Options options, Point origin, int width, int height, bool sync)
    {
      // Encode once (a start marker carries the run's metadata), then draw from the modules
      if (!m_generator.TryGenerateModules(payload, m_start, m_modules, out var matrix))
        return;
      if (m_renderMode == FrameMarkerRenderMode.Bitmap)
      {
        var texture = sync ? m_syncTexture ??= new FrameMarkerTexture() : m_mainTexture ??= new FrameMarkerTexture();
        if (texture.Update(matrix, options.QuietZoneModules))
          texture.DrawNow(options, origin, width, height);
        return;
      }
      if (m_renderMode == FrameMarkerRenderMode.ShaderPackedBits || m_renderMode == FrameMarkerRenderMode.Shader)
      {
        bool packed = m_renderMode == FrameMarkerRenderMode.ShaderPackedBits;
        if (m_mainQuad != null && m_mainQuad.PackedBits != packed)
        {
          // The mode changed at run time: the quads are made again for it
          m_mainQuad.Dispose();
          m_syncQuad?.Dispose();
          (m_mainQuad, m_syncQuad) = (null, null);
        }
        var shader = packed ? m_packedShader : m_quadShader;
        var quad = sync ? m_syncQuad ??= new FrameMarkerQuad(packed, shader) : m_mainQuad ??= new FrameMarkerQuad(packed, shader);
        if (quad.Update(matrix, options, origin, height))
        {
          quad.DrawNow(width);
          return;
        }
        if (!m_shaderWarned)
        {
          Debug.LogWarning(
            $"FrameMarkerOverlay: shader {(packed ? FrameMarkerQuad.PackedShaderName : FrameMarkerQuad.ShaderName)} is not available (it needs shader model 3.5, and in player builds the component's reference or 'Always Included Shaders'). Drawing geometry instead.",
            this
          );
          m_shaderWarned = true;
        }
      }
      var material = GetMaterial();
      if (material == null)
        return;
      int count = Marker.ModulesToQuads(matrix, options, origin, m_quads);
      FrameMarkerGL.DrawQuads(material, m_quads.AsSpan(0, count), width, height);
    }

    private double AnimationTime() => AnimationTimeProvider != null ? AnimationTimeProvider() : Time.timeAsDouble;

    /// <summary>Unity's real time now (the marker is drawn at the end of the frame) minus the time at the beginning of the frame.</summary>
    private static uint UnityCpuBusyTicks()
    {
      double seconds = Time.realtimeSinceStartupAsDouble - Time.unscaledTimeAsDouble;
      return seconds > 0 ? (uint)Math.Min(uint.MaxValue, Marker.SecondsToTicks(seconds)) : 0u;
    }

    private long CpuStartTicks()
    {
      if (CpuStartTicksProvider != null)
        return CpuStartTicksProvider();
      if (IntendedDisplayTicksProvider != null)
        return 0;
      // The time at the beginning of this frame; 0 means unknown, so the very first frame reports 1 tick
      long ticks = Marker.SecondsToTicks(Time.unscaledTimeAsDouble);
      return ticks > 0 ? ticks : 1;
    }

    /// <summary>
    /// The frame interval Unity aims for, as the Application.targetFrameRate documentation describes it: on Android and iOS the
    /// targetFrameRate (they ignore vSyncCount; unset, they run at 30 fps); elsewhere the refresh rate divided by QualitySettings.vSyncCount
    /// while vsync is on (targetFrameRate is then ignored), else the targetFrameRate, else on the web the refresh rate. 0 if unknown: a
    /// desktop without either renders as fast as it can, and XR platforms ignore both (their SDK sets the rate).
    /// </summary>
    private static uint DefaultTargetFrameTicks()
    {
#if UNITY_2022_2_OR_NEWER
      double refreshHz = Screen.currentResolution.refreshRateRatio.value;
#else
      double refreshHz = Screen.currentResolution.refreshRate;
#endif
      int targetFps = Application.targetFrameRate;
      double fps;
      switch (Application.platform)
      {
        case RuntimePlatform.Android:
        case RuntimePlatform.IPhonePlayer:
          fps = targetFps > 0 ? targetFps : MobileDefaultFps;
          break;
        default:
          fps =
            QualitySettings.vSyncCount > 0 ? refreshHz / QualitySettings.vSyncCount
            : targetFps > 0 ? targetFps
            : Application.platform == RuntimePlatform.WebGLPlayer ? refreshHz
            : 0;
          break;
      }
      return fps > 0 ? (uint)Math.Round(Marker.TicksPerSecond / fps) : 0u;
    }

    private MarkerKind Kind()
    {
      switch (Phase)
      {
        case MarkerPhase.Start:
          return MarkerKind.SequenceStart;
        case MarkerPhase.End:
          return MarkerKind.SequenceEnd;
        default:
          return MarkerKind.Frame;
      }
    }

    private void EnterPhase(MarkerPhase phase)
    {
      Phase = phase;
      m_phaseStartTime = Time.realtimeSinceStartupAsDouble;
    }

    private void UpdatePhase()
    {
      if (Time.realtimeSinceStartupAsDouble - m_phaseStartTime < m_sequenceMarkerSeconds)
        return;
      if (Phase == MarkerPhase.Start)
        EnterPhase(MarkerPhase.Running);
      else if (Phase == MarkerPhase.End)
        EnterPhase(MarkerPhase.Idle);
    }

    private Material GetMaterial()
    {
      if (m_material != null)
        return m_material;
      if (m_ownedMaterial != null)
        return m_ownedMaterial;
      m_ownedMaterial = FrameMarkerGL.CreateMaterial();
      if (m_ownedMaterial == null && !m_warned)
      {
        Debug.LogError(
          "FrameMarkerOverlay: shader Hidden/Internal-Colored not found. Assign a material or add the shader to 'Always Included Shaders'.",
          this
        );
        m_warned = true;
      }
      return m_ownedMaterial;
    }

    private void WarnOnce(int moduleSize, int height, int storedHeight)
    {
      if (m_warned)
        return;
      m_warned = true;
      if (moduleSize < Marker.MinimumModuleSizePx(height, storedHeight))
        Debug.LogWarning(
          $"FrameMarkerOverlay: {moduleSize} px modules become fewer than 2 pixels in a {storedHeight} pixel high capture; the marker will not be readable.",
          this
        );
#if UNITY_2023_1_OR_NEWER
      if (HDROutputSettings.main.active)
        Debug.LogWarning(
          "FrameMarkerOverlay: HDR output is active. Turn it off while capturing: tone mapping changes the marker's black and white.",
          this
        );
#endif
    }
  }
}
#endif
