using System.Collections.Generic;
using UnityEngine;

// ジョッキ1個。見た目と当たり判定を生成し、ベルトに乗って左へ進む
public class BeerMug : MonoBehaviour
{
    const float Wall = 0.01f;

    const float SurfaceDepth = 0.015f; // 液面コライダーはふちからこれだけ下

    BeerConveyor conveyor;
    readonly List<Renderer> glassParts = new List<Renderer>();
    Transform beer;
    int capacity, drops;
    float width, height;

    public float Fill => (float)drops / capacity; // 0〜1
    public bool IsFull => drops >= capacity;

    public void Build(BeerConveyor c)
    {
        conveyor = c;
        capacity = Mathf.Max(1, c.dropsToFill);
        width = c.mugWidth;
        height = c.mugHeight;
        float w = width, h = height, t = Wall;

        // 本体（不透明。上から中身が見えるよう箱の壁で作る）
        Part("Bottom", new Vector3(0, t / 2, 0), new Vector3(w, t, w));
        Part("WallL", new Vector3(-(w - t) / 2, h / 2, 0), new Vector3(t, h, w));
        Part("WallR", new Vector3((w - t) / 2, h / 2, 0), new Vector3(t, h, w));
        Part("WallF", new Vector3(0, h / 2, -(w - t) / 2), new Vector3(w - 2 * t, h, t));
        Part("WallB", new Vector3(0, h / 2, (w - t) / 2), new Vector3(w - 2 * t, h, t));

        // 取っ手（右側＝進行方向の後ろ）
        float hx = w / 2 + 0.03f;
        Part("HandleV", new Vector3(hx, h * 0.5f, 0), new Vector3(0.012f, h * 0.6f, 0.02f));
        Part("HandleTop", new Vector3(w / 2 + 0.015f, h * 0.8f, 0), new Vector3(0.03f, 0.012f, 0.02f));
        Part("HandleBottom", new Vector3(w / 2 + 0.015f, h * 0.2f, 0), new Vector3(0.03f, 0.012f, 0.02f));

        // 液面コライダー（見えない。ふちのすぐ下に固定）
        var surface = new GameObject("BeerSurface");
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = new Vector3(0, h - SurfaceDepth, 0);
        var box = surface.AddComponent<BoxCollider>();
        box.size = new Vector3(w - 2 * t, 0.01f, w - 2 * t);
        surface.AddComponent<BeerSurface>().mug = this;

        // 中のビール（見た目だけ。当たり判定なし）
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = "Beer";
        Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(transform, false);
        b.GetComponent<Renderer>().sharedMaterial = c.BeerMat;
        beer = b.transform;
        UpdateBeer();
    }

    // 液面に粒が当たった。満タンなら false（あふれ）
    public bool AddDrop()
    {
        if (IsFull) return false;
        drops++;
        UpdateBeer();
        return true;
    }

    void UpdateBeer()
    {
        float inner = width - 2 * Wall - 0.002f;
        float maxH = height - Wall - SurfaceDepth;
        float bh = maxH * Fill;
        beer.gameObject.SetActive(drops > 0);
        beer.localScale = new Vector3(inner, Mathf.Max(bh, 0.001f), inner);
        beer.localPosition = new Vector3(0, Wall + bh / 2, 0);
    }

    Renderer Part(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = conveyor.GlassMat;
        glassParts.Add(r);
        return r;
    }

    void Update()
    {
        if (conveyor == null || conveyor.Halted) return;
        transform.position += Vector3.left * (conveyor.beltSpeed * Time.deltaTime);

        if (transform.position.x <= conveyor.despawnX)
            Destroy(gameObject);
    }
}
