using UnityEngine;

// 3レーンのベルトコンベアを実行時に生成し、ジョッキを右から左へ流す
public class BeerConveyor : MonoBehaviour
{
    [Header("レーン（ワールド座標）")]
    public float[] laneZ = { 0.35f, 0.55f, 0.75f }; // 手前・中央・奥
    public float beltTopY = 0.45f;   // ベルト上面の高さ
    public float startX = 1.4f;      // ジョッキが出てくる位置（右）
    public float endX = -1.0f;       // 判定する位置（左端）
    public float laneWidth = 0.18f;

    [Header("ジョッキ")]
    public float spawnInterval = 2.3f; // 出現間隔（秒）
    public float beltSpeed = 0.3f;     // 流れる速さ（m/秒）
    public int dropsToFill = 350;      // 満タンになる粒の数
    [Range(0f, 1f)] public float successRatio = 0.9f;
    public float mugWidth = 0.14f;
    public float mugHeight = 0.18f;

    public bool Halted { get; private set; }

    // ジョッキが使う共有マテリアル
    public Material GlassMat { get; private set; }
    public Material BeerMat { get; private set; }

    bool spawning;
    float timer;
    int lastLane = -1;
    Material baseMat;

    void Start()
    {
        BuildBelt();
        Begin(); // 段階1〜3：開始と同時に流す
    }

    public void Begin()
    {
        spawning = true;
        timer = 0f;
    }

    public void StopSpawning() => spawning = false;

    // 全体を止める（リザルト中）
    public void Halt()
    {
        spawning = false;
        Halted = true;
    }

    void Update()
    {
        if (!spawning) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer += spawnInterval;
            Spawn();
        }
    }

    void Spawn()
    {
        if (laneZ.Length == 0) return;
        int lane = Random.Range(0, laneZ.Length);
        if (laneZ.Length > 1 && lane == lastLane)          // 同じレーンが続きにくくする
            lane = (lane + Random.Range(1, laneZ.Length)) % laneZ.Length;
        lastLane = lane;

        var go = new GameObject("BeerMug");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(startX, beltTopY, laneZ[lane]);
        go.AddComponent<BeerMug>().Build(this);
    }

    void BuildBelt()
    {
        float length = Mathf.Abs(startX - endX) + 0.4f;
        float midX = (startX + endX) * 0.5f;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (float z in laneZ)
        {
            var belt = Box("Belt", new Vector3(midX, beltTopY - 0.025f, z),
                           new Vector3(length, 0.05f, laneWidth), true);
            if (baseMat == null) baseMat = belt.sharedMaterial;
            belt.sharedMaterial = MakeMat(new Color(0.15f, 0.15f, 0.17f));
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        // 台（ベルトの下と、レーンのすき間を受ける）
        float frameH = beltTopY - 0.05f;
        var frame = Box("Frame", new Vector3(midX, frameH * 0.5f, (minZ + maxZ) * 0.5f),
                        new Vector3(length, frameH, maxZ - minZ + laneWidth + 0.04f), true);
        if (baseMat == null) baseMat = frame.sharedMaterial;
        frame.sharedMaterial = MakeMat(new Color(0.45f, 0.45f, 0.5f));

        GlassMat = MakeMat(new Color(0.85f, 0.9f, 0.95f));
        BeerMat = MakeMat(new Color(1f, 0.75f, 0.1f));
    }

    Renderer Box(string name, Vector3 pos, Vector3 size, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        if (!collider) Destroy(go.GetComponent<Collider>());
        return go.GetComponent<Renderer>();
    }

    public Material MakeMat(Color c)
    {
        var m = new Material(baseMat);
        m.color = c;
        return m;
    }
}
