using UnityEngine;

/// <summary>
/// Marks an indoor space: rain drops that enter this box vanish at once (PSXRain kills them),
/// so fast drops can never slip through a thin roof. Put it on a trigger BoxCollider that fills the room.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class PSXNoRainZone : MonoBehaviour
{
    void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        gameObject.layer = 2; // Ignore Raycast: never blocks the player's aim or the rain's ground checks
    }
}
