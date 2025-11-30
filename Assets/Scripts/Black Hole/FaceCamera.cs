using UnityEngine;

public class FaceCamera : MonoBehaviour
{
    private Camera _cam;

    // Pour un Plane Unity classique qui doit être à -90° en X
    public Vector3 rotationOffset = new Vector3(-90f, 0f, 0f);

    void Start()
    {
        _cam = Camera.main;
    }

    void LateUpdate()
    {
        if (_cam == null) return;

        // Regarde la caméra
        Vector3 dir = _cam.transform.position - transform.position;
        Quaternion lookRotation = Quaternion.LookRotation(-dir);

        // On applique l'offset pour compenser l'orientation du mesh
        transform.rotation = lookRotation * Quaternion.Euler(rotationOffset);
    }
}