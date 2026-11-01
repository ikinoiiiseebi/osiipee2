using UnityEngine;

// ジョッキ1個（円柱）。見た目と当たり判定を生成し、ベルトに乗って左へ進む
// 当たり判定：液面（ふちのすぐ下の薄い円柱）だけが「注げた」。壁・底・取っ手は外れ
public class BeerMug : MonoBehaviour
{
    const float Wall = 0.01f;          // 壁の厚さ
    const int WallSegments = 20;       // 円周に並べる壁の枚数
    const float SurfaceDepth = 0.015f; // 液面コライダーはふちからこれだけ下
    const float PopTime = 0.4f;

    static Mesh cylinderMesh;          // Unity 標準の円柱メッシュ（高さ2・直径1）

    BeerConveyor conveyor;
    Transform beer;
    int capacity, drops;
    bool judged;
    float popTimer;
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
        float radius = w / 2;
        float inner = w - 2 * t;

        if (cylinderMesh == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinderMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(tmp);
        }

        // 底（円柱を薄くしたもの）
        Cylinder("Bottom", new Vector3(0, t / 2, 0), w, t, c.GlassMat, true);

        // 壁：薄い箱を円形に並べる（中は空洞なので注げる）
        float segWidth = 2f * Mathf.PI * radius / WallSegments * 1.15f; // すき間ができないよう少し重ねる
        for (int i = 0; i < WallSegments; i++)
        {
            float a = i * Mathf.PI * 2f / WallSegments;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Box("Wall", dir * (radius - t / 2) + Vector3.up * (h / 2),
                Quaternion.LookRotation(dir), new Vector3(segWidth, h, t));
        }

        // 取っ手（右側＝進行方向の後ろ）
        float hx = radius + 0.03f;
        Box("HandleV", new Vector3(hx, h * 0.5f, 0), Quaternion.identity, new Vector3(0.012f, h * 0.6f, 0.02f));
        Box("HandleTop", new Vector3(radius + 0.012f, h * 0.8f, 0), Quaternion.identity, new Vector3(0.036f, 0.012f, 0.02f));
        Box("HandleBottom", new Vector3(radius + 0.012f, h * 0.2f, 0), Quaternion.identity, new Vector3(0.036f, 0.012f, 0.02f));

        // 液面コライダー（見えない薄い円柱。ふちのすぐ下に固定）
        var surface = new GameObject("BeerSurface");
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = new Vector3(0, h - SurfaceDepth, 0);
        surface.transform.localScale = new Vector3(inner, 0.005f, inner); // 厚さ 0.01
        var mc = surface.AddComponent<MeshCollider>();
        mc.sharedMesh = cylinderMesh;
        mc.convex = true;
        surface.AddComponent<BeerSurface>().mug = this;

        // 中のビール（円柱。見た目だけ）
        beer = Cylinder("Beer", Vector3.zero, inner - 0.002f, 0.001f, c.BeerMat, false).transform;
        UpdateBeer();
    }

    // 液面に粒が当たった。満タンなら false（あふれ）
    public bool AddDrop()
    {
        if (IsFull) return false;
        drops++;
        UpdateBeer();

        // 流れている途中でも9割を超えた瞬間に成功：ふくらんでクラッカー
        if (!judged && Fill >= conveyor.successRatio)
        {
            judged = true;
            popTimer = PopTime;
            Confetti.Burst(transform.position + Vector3.up * height, conveyor.ConfettiMats);
            conveyor.Report(this, true);
        }
        return true;
    }

    void UpdateBeer()
    {
        float inner = width - 2 * Wall - 0.002f;
        float maxH = height - Wall - SurfaceDepth;
        float bh = Mathf.Max(maxH * Fill, 0.001f);
        beer.gameObject.SetActive(drops > 0);
        beer.localScale = new Vector3(inner, bh / 2f, inner); // 円柱メッシュは高さ2
        beer.localPosition = new Vector3(0, Wall + bh / 2, 0);
    }

    GameObject Box(string name, Vector3 pos, Quaternion rot, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = conveyor.GlassMat;
        return go;
    }

    // 円柱（直径 d、高さ h）。collider=true なら円柱形の当たり判定を付ける
    GameObject Cylinder(string name, Vector3 pos, float d, float h, Material mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>()); // 標準はカプセル形なので外す
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(d, h / 2f, d);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (collider)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = cylinderMesh;
            mc.convex = true;
        }
        return go;
    }

    void Update()
    {
        // 成功したときにふくらむ（ぽよん）
        if (popTimer > 0f)
        {
            popTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(popTimer / PopTime);
            transform.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(t * Mathf.PI));
        }

        if (conveyor == null || conveyor.Halted) return;
        transform.position += Vector3.left * (conveyor.CurrentSpeed * Time.deltaTime);

        // 9割に届かないまま左端のラインを越えたら失敗
        if (!judged && transform.position.x <= conveyor.endX)
        {
            judged = true;
            conveyor.Report(this, false);
        }

        if (transform.position.x <= conveyor.despawnX)
            Destroy(gameObject);
    }
}
