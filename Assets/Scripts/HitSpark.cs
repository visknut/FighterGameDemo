using UnityEngine;

// A spinning cube that pops and shrinks at the point of impact.
public class HitSpark : MonoBehaviour
{
    const float Life = 0.2f;

    float age;
    float size;
    Material mat;

    public static void Spawn(Vector3 position, Color color, float size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "HitSpark";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, Random.Range(0f, 90f)));
        Renderer r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        HitSpark spark = go.AddComponent<HitSpark>();
        spark.mat = r.material;
        spark.mat.color = color;
        spark.size = size;
        go.transform.localScale = Vector3.one * size;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Life;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = Vector3.one * size * (1f + 0.6f * t) * (1f - t);
        transform.Rotate(0f, 0f, 720f * Time.deltaTime);
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
