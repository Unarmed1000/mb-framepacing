//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sample: turns the GameObject (for example the camera) for a few seconds and brackets the turn with start and end markers, so a capture
//* measures exactly that part. Needs a FrameMarkerOverlay in the scene.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections;
using MB.FrameMarker.Unity;
using UnityEngine;

namespace MB.FrameMarker.Samples
{
  public sealed class BenchmarkRun : MonoBehaviour
  {
    [SerializeField]
    private FrameMarkerOverlay m_overlay;

    [SerializeField]
    private string m_runName = "camera pan";

    [Tooltip("Wait this long before the run so the capture tool is recording")]
    [SerializeField]
    private float m_delaySeconds = 2f;

    [SerializeField]
    private float m_runSeconds = 10f;

    [SerializeField]
    private float m_degreesPerSecond = 30f;

    private IEnumerator Start()
    {
      if (m_overlay == null)
        m_overlay = FindAnyObjectByType<FrameMarkerOverlay>();
      yield return new WaitForSecondsRealtime(m_delaySeconds);
      yield return m_overlay.RunFor(m_runName, m_runSeconds);
    }

    private void Update()
    {
      if (m_overlay != null && m_overlay.Phase == MarkerPhase.Running)
        transform.Rotate(0f, m_degreesPerSecond * Time.deltaTime, 0f);
    }
  }
}
