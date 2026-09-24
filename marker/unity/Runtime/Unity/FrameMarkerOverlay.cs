//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Add this component to any GameObject and every frame shows the marker: drawn at the end of the frame (after post processing, upscaling
//* and UI) straight into the output, pixel exact, in pure black and white. The frame index is Time.frameCount and the animation time is
//* Time.timeAsDouble unless AnimationTimeProvider supplies the game's own clock. BeginRun / EndRun (or the RunFor coroutine) bracket the
//* part to measure with the start and end markers. Nothing is allocated per frame.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace MB.FrameMarker.Unity
{
  [DisallowMultipleComponent]
  [AddComponentMenu("MB/Frame Marker Overlay")]
  public sealed class FrameMarkerOverlay : MonoBehaviour
  {
    // How long the start and end markers stay on screen: a little over the 250 ms minimum (doc/marker-format.md "Test sequences")
    private const double SequenceMarkerSeconds = 0.3;

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
    [SerializeField]
    private MarkerSlot m_slot = MarkerSlot.TopLeft;

    [Tooltip("Also draw frame markers in the middle and at the bottom, so the analysis can detect tearing.")]
    [SerializeField]
    private bool m_tearingMarkers;

    [Tooltip("Draw frame markers (run id 0) while no run is active.")]
    [SerializeField]
    private bool m_drawWhenIdle = true;

    [Tooltip("Optional: an unlit vertex color material without blending, depth test or culling. Empty = Hidden/Internal-Colored.")]
    [SerializeField]
    private Material m_material;

    private static readonly MarkerSlot[] g_tearingSlots = { MarkerSlot.TopLeft, MarkerSlot.MiddleLeft, MarkerSlot.BottomLeft };

    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly Quad[] m_quads = new Quad[Marker.MaxQuadCount];
    private WaitForEndOfFrame m_endOfFrame;
    private Coroutine m_drawing;
    private Material m_ownedMaterial;
    private StartMetadata m_start;
    private double m_phaseStartTime;
    private bool m_warned;

    /// <summary>The game's animation clock in seconds. Null = Time.timeAsDouble.</summary>
    public Func<double> AnimationTimeProvider { get; set; }

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

    /// <summary>Start a measured run: the start marker (with the name and the current time) is shown first, then the frame markers.</summary>
    public void BeginRun(string name)
    {
      ++RunId;
      m_start = StartMetadata.Create(DateTime.UtcNow, FitName(name));
      EnterPhase(MarkerPhase.Start);
    }

    /// <summary>End the run: the end marker is shown, then the overlay goes back to idle.</summary>
    public void EndRun()
    {
      if (Phase == MarkerPhase.Start || Phase == MarkerPhase.Running)
        EnterPhase(MarkerPhase.End);
    }

    /// <summary>Measure <paramref name="seconds"/> (real time) as a run named <paramref name="name"/>: start it from a coroutine.</summary>
    public IEnumerator RunFor(string name, double seconds)
    {
      BeginRun(name);
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
      var material = GetMaterial();
      if (material == null)
        return;

      int width = Screen.width;
      int height = Screen.height;
      int storedHeight = m_storedHeight > 0 ? m_storedHeight : height;
      int moduleSize = m_moduleSizePx > 0 ? m_moduleSizePx : Marker.RecommendModuleSizePx(height, storedHeight, m_mjpeg);
      var options = new Options(moduleSize);
      // Integer downscales keep module edges on stored pixel edges when the origin is a multiple of the ratio
      int align = height % storedHeight == 0 ? height / storedHeight : 1;
      WarnOnce(moduleSize, height, storedHeight);

      var payload = new Payload((ulong)Time.frameCount, Marker.SecondsToTicks(AnimationTime()), Phase == MarkerPhase.Idle ? 0u : RunId, Kind());

      if (m_tearingMarkers && payload.Kind == MarkerKind.Frame)
      {
        // Sequence markers only at the primary slot: the start marker is larger and would overlap the middle marker
        foreach (var slot in g_tearingSlots)
          DrawMarker(material, payload, options, Marker.RecommendedOrigin(slot, width, height, options, align), width, height);
      }
      else
      {
        DrawMarker(material, payload, options, Marker.RecommendedOrigin(m_slot, width, height, options, align), width, height);
      }
    }

    private void DrawMarker(Material material, in Payload payload, in Options options, Point origin, int width, int height)
    {
      int count =
        payload.Kind == MarkerKind.SequenceStart
          ? m_generator.GenerateStartQuads(payload, m_start, options, origin, m_quads)
          : m_generator.GenerateQuads(payload, options, origin, m_quads);
      FrameMarkerGL.DrawQuads(material, m_quads, count, width, height);
    }

    private double AnimationTime() => AnimationTimeProvider != null ? AnimationTimeProvider() : Time.timeAsDouble;

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
      if (Time.realtimeSinceStartupAsDouble - m_phaseStartTime < SequenceMarkerSeconds)
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

    private static string FitName(string name)
    {
      // The start marker carries at most 64 bytes of UTF-8
      if (string.IsNullOrEmpty(name) || Encoding.UTF8.GetByteCount(name) <= Marker.MaxStartNameBytes)
        return name;
      int length = name.Length;
      while (length > 0 && Encoding.UTF8.GetByteCount(name.Substring(0, length)) > Marker.MaxStartNameBytes)
        --length;
      if (length > 0 && char.IsHighSurrogate(name[length - 1]))
        --length;
      return name.Substring(0, length);
    }
  }
}
#endif
