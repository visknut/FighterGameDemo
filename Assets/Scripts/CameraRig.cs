using UnityEngine;

// Side-on camera that tracks the midpoint between fighters, clamped to the stage, with shake.
[RequireComponent(typeof(Camera))]
public class CameraRig : MonoBehaviour
{
    public MatchManager match;
    public float height = 3f;
    public float distance = 15f;
    public float pitch = 4f;
    public float followSharpness = 8f;

    Camera cam;
    Vector3 current;
    bool initialized;
    float shakeTime, shakeAmp;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    public void Shake(float amplitude, float duration)
    {
        shakeAmp = Mathf.Max(shakeAmp, amplitude);
        shakeTime = Mathf.Max(shakeTime, duration);
    }

    void LateUpdate()
    {
        if (match == null || match.player1 == null || match.player2 == null) return;
        Fighter a = match.player1, b = match.player2;

        float mid = (a.pos.x + b.pos.x) * 0.5f;
        float halfH = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float limit = Mathf.Max(0f, match.stageHalfWidth - halfH * cam.aspect);
        float x = Mathf.Clamp(mid, -limit, limit);
        float y = height + Mathf.Max(0f, Mathf.Max(a.pos.y, b.pos.y) - 1.5f) * 0.35f;
        Vector3 target = new Vector3(x, y, -distance);

        if (!initialized)
        {
            current = target;
            initialized = true;
        }
        current = Vector3.Lerp(current, target, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));

        Vector3 shake = Vector3.zero;
        if (shakeTime > 0f)
        {
            shake = Random.insideUnitSphere * shakeAmp;
            shake.z = 0f;
            shakeTime -= Time.unscaledDeltaTime;
            if (shakeTime <= 0f) shakeAmp = 0f;
        }
        transform.SetPositionAndRotation(current + shake, Quaternion.Euler(pitch, 0f, 0f));
    }
}
