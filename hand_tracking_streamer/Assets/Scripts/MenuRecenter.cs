using UnityEngine;
using System.Collections;

public class MenuRecenter : MonoBehaviour
{
    [Tooltip("How far away from the head should the menu appear?")]
    public float distanceFromHead = 1.0f;

    [Tooltip("How high relative to the head? (0 = eye level, -0.2 = slightly below)")]
    public float heightOffset = -0.1f;

    private IEnumerator Start()
    {
        // Wait 1 frame to ensure the OVR Camera has initialized its tracking position
        yield return null;

        Recenter();
    }

    public void Recenter()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // 1. Calculate the target position
        // Start at the head position
        Vector3 targetPos = mainCam.transform.position;
        
        // Project a point forward based on where the head is looking
        // We flatten the forward vector (y=0) so the menu doesn't tilt up/down if you look at the ceiling
        Vector3 flatForward = mainCam.transform.forward;
        flatForward.y = 0;
        flatForward.Normalize();

        // Move it 'distance' meters away along that flat forward line
        targetPos += flatForward * distanceFromHead;

        // Apply height offset (so it spawns slightly below eye level usually)
        targetPos.y += heightOffset;

        // 2. Apply position
        transform.position = targetPos;

        // 3. Make the menu face the user
        // Look at the camera, then flip 180 because UI usually faces "backwards" relative to lookAt
        transform.LookAt(transform.position + flatForward);
    }
}