using UnityEngine;
using System.Collections;

public class MenuRecenter : MonoBehaviour
{
    [Tooltip("How far away from the head should the menu appear?")]
    public float distanceFromHead = 1.0f;

    [Tooltip("How high relative to the head? (0 = eye level, -0.2 = slightly below)")]
    public float heightOffset = -0.1f;

    // By making Start an IEnumerator, Unity treats it as a Coroutine automatically
    private IEnumerator Start()
    {
        // 0.5 seconds is usually enough for the OVR Rig to get a valid floor-to-head height
        yield return new WaitForSeconds(0.5f);

        Recenter();
    }

    public void Recenter()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // 1. Calculate the target position
        Vector3 targetPos = mainCam.transform.position;
        
        // Project forward and flatten Y
        Vector3 flatForward = mainCam.transform.forward;
        flatForward.y = 0;
        flatForward.Normalize();

        // Offset position
        targetPos += flatForward * distanceFromHead;
        targetPos.y += heightOffset;

        // 2. Apply position
        transform.position = targetPos;

        // 3. Make the menu face the user
        transform.LookAt(transform.position + flatForward);
    }
}