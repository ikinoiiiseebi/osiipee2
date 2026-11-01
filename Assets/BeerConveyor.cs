using System.Collections.Generic;
using UnityEngine;

// 3レーンのベルトコンベアを実行時に生成し、ジョッキを右から左へ流す
public class BeerConveyor : MonoBehaviour
{
    [Header("レーン（ワールド座標）")]
    public float[] laneZ = { 0.35f, 0.55f, 0.75f }; // 手前・中央・奥
    public float beltTopY = 0.45f;   // ベルト上面の高さ
    public float spawnX = 1.9f;      // ジョッキが出てくる位置（右・画面の外）
    public float endX = -1.0f;       // 判定する位置（左端）
    public float despawnX = -1.9f;   // ジョッキが消える位置（左・画面の外）
    public float beltHalfLength = 2.8f; // ベルトは X = ±この値 まで（画面の外まで伸ばす）
    public float laneWidth = 0.18f;
    public float prewarmMinX = -0.3f;   // 開始前にここより右へジョッキを並べておく

    [Header("ジョッキ")]
    public float spawnInterval = 2.3f; // 出現間隔（秒）
    public float beltSpeed = 0.3f;     // 流れる速さ（m/秒）
    public int dropsToFill = 350;      // 満タンになる粒の数
    [Range(0f, 1f)] public float successRatio = 0.9f;
    public float mugWidth = 0.14f;
    public float mugHeight = 0.18f;

    // 判定の結果（左端に着いた数と成功数）
    public int Finished { get; private set; }
    public int Succeeded { get; private set; }

    // 止まっている間（カウントダウン中・リザルト中）はベルトが動かない
    public bool Halted { get; private set; } = true;

    // ジョッキが使う共有マテリアル
    public Material GlassMat { get; private set; }
    public Material BeerMat { get; private set; }
    public Material SuccessMat { get; private set; }
    public Material FailMat { get; private set; }

    struct Popup { public Vector3 pos; public string text; public Color color; public float time; }
    readonly List<Popup> popups = new List<Popup>();
    const float PopupTime = 1.2f;

    bool spawning;
    float timer;
    int lastLane = -1;
    Material baseMat;

    void Start()
    {
        BuildBelt();
        Prewarm();
    }

    // カウントダウン明けに呼ぶ：ベルトを動かし、出現を始める
    public void Begin()
    {
        Halted = false;
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

    // 左端に着いたジョッキから呼ばれる
    public void Report(BeerMug mug, bool success)
    {
        Finished++;
        if (success) Succeeded++;
        popups.Add(new Popup
        {
            pos = mug.transform.position + Vector3.up * (mugHeight + 0.08f),
            text = success ? "成功！" : "失敗…",
            color = success ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.35f, 0.3f),
            time = Time.time
        });
    }

    void Update()
    {
        if (!spawning) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer += spawnInterval;
            Spawn(spawnX);
        }
    }

    // 開始時からベルトにジョッキが並んでいるようにする
    void Prewarm()
    {
        float spacing = beltSpeed * spawnInterval;
        if (spacing <= 0.01f) return;
        for (float x = spawnX - spacing; x >= prewarmMinX; x -= spacing)
            Spawn(x);
    }

    void Spawn(float x)
    {
        if (laneZ.Length == 0) return;
        int lane = Random.Range(0, laneZ.Length);
        if (laneZ.Length > 1 && lane == lastLane)          // 同じレーンが続きにくくする
            lane = (lane + Random.Range(1, laneZ.Length)) % laneZ.Length;
        lastLane = lane;

        var go = new GameObject("BeerMug");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(x, beltTopY, laneZ[lane]);
        go.AddComponent<BeerMug>().Build(this);
    }

    void BuildBelt()
    {
        float length = beltHalfLength * 2f;
        float midX = 0f;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (float z in laneZ)
        {
            var belt = Box("Belt", new Vector3(midX, beltTopY - 0.025f, z),
                           new Vector3(length, 0.05f, laneWidth));
            if (baseMat == null) baseMat = belt.sharedMaterial;
            belt.sharedMaterial = MakeMat(new Color(0.15f, 0.15f, 0.17f));
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        // 台（ベルトの下と、レーンのすき間を受ける）
        float frameH = beltTopY - 0.05f;
        var frame = Box("Frame", new Vector3(midX, frameH * 0.5f, (minZ + maxZ) * 0.5f),
                        new Vector3(length, frameH, maxZ - minZ + laneWidth + 0.04f));
        if (baseMat == null) baseMat = frame.sharedMaterial;
        frame.sharedMaterial = MakeMat(new Color(0.45f, 0.45f, 0.5f));

        // 判定ライン（左端）の目印。当たり判定なし
        var line = Box("JudgeLine", new Vector3(endX, beltTopY + 0.002f, (minZ + maxZ) * 0.5f),
                       new Vector3(0.02f, 0.004f, maxZ - minZ + laneWidth));
        Destroy(line.GetComponent<Collider>());
        line.sharedMaterial = MakeMat(new Color(1f, 0.9f, 0.2f));

        GlassMat = MakeMat(new Color(0.85f, 0.9f, 0.95f));
        BeerMat = MakeMat(new Color(1f, 0.75f, 0.1f));
        SuccessMat = MakeMat(new Color(0.2f, 0.9f, 0.3f));
        FailMat = MakeMat(new Color(0.9f, 0.2f, 0.2f));
    }

    Renderer Box(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        return go.GetComponent<Renderer>();
    }

    public Material MakeMat(Color c)
    {
        var m = new Material(baseMat);
        m.color = c;
        return m;
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 44,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float age = Time.time - p.time;
            if (age > PopupTime) { popups.RemoveAt(i); continue; }

            Vector3 sp = cam.WorldToScreenPoint(p.pos);
            if (sp.z <= 0f) continue;
            float y = Screen.height - sp.y - age * 60f;   // 少しずつ上へ
            style.normal.textColor = new Color(p.color.r, p.color.g, p.color.b, 1f - age / PopupTime);
            GUI.Label(new Rect(sp.x - 150, y - 30, 300, 60), p.text, style);
        }
    }
}
