using UnityEngine;

// 成功したときのクラッカー（色とりどりの紙吹雪）。小さな箱を飛ばして1秒ほどで消す
public class Confetti : MonoBehaviour
{
    const int Count = 30;
    const float Life = 1.2f;
    const float Gravity = 6f;

    Transform[] pieces;
    Vector3[] velocities;
    Vector3[] spins;
    float age;

    public static void Burst(Vector3 pos, Material[] mats)
    {
        var go = new GameObject("Confetti");
        go.transform.position = pos;
        go.AddComponent<Confetti>().Build(mats);
    }

    void Build(Material[] mats)
    {
        pieces = new Transform[Count];
        velocities = new Vector3[Count];
        spins = new Vector3[Count];
        for (int i = 0; i < Count; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(transform, false);
            p.transform.localScale = new Vector3(0.025f, 0.004f, 0.015f);
            p.transform.localRotation = Random.rotation;
            if (mats != null && mats.Length > 0)
                p.GetComponent<Renderer>().sharedMaterial = mats[Random.Range(0, mats.Length)];
            pieces[i] = p.transform;

            Vector2 side = Random.insideUnitCircle * 0.9f;
            velocities[i] = new Vector3(side.x, Random.Range(1.8f, 2.8f), side.y);
            spins[i] = Random.insideUnitSphere * 720f;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age >= Life) { Destroy(gameObject); return; }
        for (int i = 0; i < Count; i++)
        {
            velocities[i].y -= Gravity * dt;
            velocities[i] *= 1f - 1.5f * dt;  // 空気抵抗でふわっと
            pieces[i].position += velocities[i] * dt;
            pieces[i].Rotate(spins[i] * dt);
        }
    }
}
