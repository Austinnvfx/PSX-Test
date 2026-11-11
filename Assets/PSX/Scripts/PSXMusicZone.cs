using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An area where music plays. Full volume inside <see cref="innerRadius"/>, fading out to silence at
/// <see cref="outerRadius"/> (measured flat, ignoring height). PSXAudioDirector does the playing.
/// Move, resize or duplicate these freely; the circles show in the Scene view.
/// </summary>
[ExecuteAlways]
public class PSXMusicZone : MonoBehaviour
{
    public static readonly List<PSXMusicZone> All = new List<PSXMusicZone>();

    [Tooltip("Track for this zone. Empty = the director's default music.")]
    public AudioClip track;
    public float innerRadius = 25f;
    public float outerRadius = 50f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    /// <summary>0 outside, 1 inside the inner radius, smooth in between.</summary>
    public float WeightAt(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        float outer = Mathf.Max(outerRadius, innerRadius + 0.01f);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, outer, d.magnitude));
    }

    void OnDrawGizmos()
    {
        DrawCircle(innerRadius, new Color(0.9f, 0.3f, 0.9f, 0.9f));
        DrawCircle(outerRadius, new Color(0.9f, 0.3f, 0.9f, 0.35f));
    }

    void DrawCircle(float r, Color c)
    {
        Gizmos.color = c;
        Vector3 center = transform.position + Vector3.up * 0.5f;
        Vector3 prev = center + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= 64; i++)
        {
            float a = i / 64f * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
