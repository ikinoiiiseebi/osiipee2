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
    public float prewarmNearestX = 0.5f; // 開始前に並べておくジョッキは、この位置より右だけ（画面の中央より右）

    [Header("ジョッキ")]
    public float spawnInterval = 2.3f; // 開始時の出現間隔（秒）
    public float beltSpeed = 0.3f;     // 開始時の流れる速さ（m/秒）

    [Header("だんだん速くなる（出現間隔は ジョッキの間の距離 が一定になるよう自動で短くなる）")]
    public float maxBeltSpeed = 0.3f;     // 最高速度（m/秒）
    public float speedRampSeconds = 60f;  // 開始時の速さから最高速度になるまでの秒数
    public int dropsToFill = 350;      // 満タンになる粒の数
    [Range(0f, 1f)] public float successRatio = 0.9f;
    public float mugWidth = 0.14f;
    public float mugHeight = 0.18f;

    // 判定の結果（左端に着いた数と成功数）
    public int Finished { get; private set; }
    public int Succeeded { get; private set; }
    public int Combo { get; private set; }           // 連続成功数（失敗で0）

    // 今の速さ（ジョッキもベルトの模様もこの速さで動く）
    public float CurrentSpeed { get; private set; }
    float runTime;                                   // ベルトが動いている時間
    float Spacing => beltSpeed * spawnInterval;      // ジョッキの間の距離（一定）

    // 止まっている間（カウントダウン中・リザルト中）はベルトが動かない
    public bool Halted { get; private set; } = true;

    // ジョッキが使う共有マテリアル
    public Material GlassMat { get; private set; }
    public Material BeerMat { get; private set; }
    public Material[] ConfettiMats { get; private set; }

    struct Popup { public Vector3 pos; public string text; public Color color; public float time; }
    readonly List<Popup> popups = new List<Popup>();
    const float PopupTime = 1.2f;

    bool spawning;
    float timer;
    int lastLane = -1;
    Material baseMat;
    Material beltMat;
    const float BeltTile = 0.2f;   // ベルトの模様1枚分の長さ（m）

    [Header("見た目")]
    public float beltScrollSign = 1f; // ベルトの模様が逆向きに流れて見えたら -1 にする

    void Start()
    {
        CurrentSpeed = beltSpeed;
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
        if (success) { Succeeded++; Combo++; }
        else Combo = 0;

        string text = !success ? "失敗…" : Combo >= 2 ? $"{Combo}連続！" : "成功！";
        popups.Add(new Popup
        {
            pos = mug.transform.position + Vector3.up * (mugHeight + 0.08f),
            text = text,
            color = !success ? Ui.Alert : Combo >= 3 ? Ui.Border : Ui.Beer,
            time = Time.time
        });
    }

    void Update()
    {
        if (Halted) return;

        // だんだん速くする（speedRampSeconds かけて beltSpeed → maxBeltSpeed）
        runTime += Time.deltaTime;
        float k = speedRampSeconds > 0f ? Mathf.Clamp01(runTime / speedRampSeconds) : 1f;
        CurrentSpeed = Mathf.Lerp(beltSpeed, Mathf.Max(beltSpeed, maxBeltSpeed), k);

        // ベルトの模様を流れる速さに合わせて動かす
        if (beltMat != null)
        {
            var o = beltMat.mainTextureOffset;
            // 箱の上面はテクスチャの向きが X と逆なので、マイナス方向にずらすとジョッキと同じ向きに流れる
            o.x = (o.x - beltScrollSign * CurrentSpeed * Time.deltaTime / BeltTile) % 1f;
            beltMat.mainTextureOffset = o;
        }

        if (!spawning) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            // 速くなっても間隔（距離）が一定になるよう、出現間隔を短くする
            timer += Spacing / Mathf.Max(0.01f, CurrentSpeed);
            Spawn(spawnX);
        }
    }

    // 開始時からベルトにジョッキが並んでいるようにする
    void Prewarm()
    {
        float spacing = Spacing;
        if (spacing <= 0.01f) return;
        for (float x = spawnX - spacing; x >= prewarmNearestX; x -= spacing)
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

        // 回転寿司風：ステンレスの板が流れるベルト（模様は速さに合わせてスクロール）
        beltMat = ProcTex.Mat(new Color(0.5f, 0.5f, 0.53f), ProcTex.Belt(64),
                              new Vector2(length / BeltTile, 1f), 0.6f);
        var rail = ProcTex.Mat(new Color(0.85f, 0.85f, 0.88f), null, Vector2.one, 0.8f);
        foreach (float z in laneZ)
        {
            var belt = Box("Belt", new Vector3(midX, beltTopY - 0.025f, z),
                           new Vector3(length, 0.05f, laneWidth));
            if (baseMat == null) baseMat = belt.sharedMaterial;
            belt.sharedMaterial = beltMat;
            // 両側のステンレスのレール（飾り。当たり判定なし）
            foreach (float side in new[] { -1f, 1f })
            {
                var r = Box("Rail", new Vector3(midX, beltTopY + 0.004f, z + side * (laneWidth / 2f - 0.005f)),
                            new Vector3(length, 0.012f, 0.01f));
                Destroy(r.GetComponent<Collider>());
                r.sharedMaterial = rail;
            }
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        // 台＝木の枠のカウンター（ベルトの下と、レーンのすき間を受ける）
        float frameH = beltTopY - 0.05f;
        float depth = maxZ - minZ + laneWidth + 0.04f;
        var frame = Box("Frame", new Vector3(midX, frameH * 0.5f, (minZ + maxZ) * 0.5f),
                        new Vector3(length, frameH, depth));
        if (baseMat == null) baseMat = frame.sharedMaterial;
        frame.sharedMaterial = ProcTex.Mat(new Color(0.5f, 0.32f, 0.19f), ProcTex.Wood(256, 6, 5f),
                                           new Vector2(length / 1.2f, 1f), 0.3f);

        // カウンターの手前と奥のふち（白木。飾り。当たり判定なし）
        var light = ProcTex.Mat(new Color(0.9f, 0.74f, 0.52f), ProcTex.Wood(128, 2, 9f),
                                new Vector2(length / 0.8f, 1f), 0.35f);
        foreach (float ez in new[] { minZ - laneWidth / 2f - 0.02f, maxZ + laneWidth / 2f + 0.02f })
        {
            var edge = Box("CounterEdge", new Vector3(midX, beltTopY - 0.02f, ez),
                           new Vector3(length, 0.07f, 0.04f));
            Destroy(edge.GetComponent<Collider>());
            edge.sharedMaterial = light;
        }

        // 判定ライン（左端）の目印。当たり判定なし
        var line = Box("JudgeLine", new Vector3(endX, beltTopY + 0.002f, (minZ + maxZ) * 0.5f),
                       new Vector3(0.02f, 0.004f, maxZ - minZ + laneWidth));
        Destroy(line.GetComponent<Collider>());
        line.sharedMaterial = MakeMat(new Color(1f, 0.9f, 0.2f));

        GlassMat = MakeMat(new Color(0.85f, 0.9f, 0.95f));
        BeerMat = MakeMat(new Color(1f, 0.75f, 0.1f));
        ConfettiMats = new[]
        {
            MakeMat(new Color(1f, 0.25f, 0.3f)),
            MakeMat(new Color(1f, 0.85f, 0.1f)),
            MakeMat(new Color(0.2f, 0.85f, 0.35f)),
            MakeMat(new Color(0.25f, 0.55f, 1f)),
            MakeMat(new Color(1f, 0.45f, 0.85f)),
            MakeMat(Color.white),
        };
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
        GuiScale.Apply();

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float age = Time.time - p.time;
            if (age > PopupTime) { popups.RemoveAt(i); continue; }

            if (cam.WorldToScreenPoint(p.pos).z <= 0f) continue;
            Vector2 gp = GuiScale.WorldToGui(cam, p.pos);
            // 画面の端で見切れないよう内側に寄せる（文字の幅 約150 + 余白）
            float x = Mathf.Clamp(gp.x, 90f, GuiScale.Width - 90f);
            float y = Mathf.Clamp(gp.y - age * 60f, 40f, GuiScale.Height - 40f);   // 少しずつ上へ
            var col = new Color(p.color.r, p.color.g, p.color.b, 1f - age / PopupTime);
            float s = 1f + 0.5f * Mathf.Max(0f, 1f - age / 0.2f);   // 出た瞬間だけ大きく
            var m = Ui.BeginScale(new Vector2(x, y), s);
            Ui.Outlined(new Rect(x - 150, y - 30, 300, 60), p.text, 44, col, Ui.Shadow, TextAnchor.MiddleCenter, 3f);
            Ui.EndScale(m);
        }
    }
}
