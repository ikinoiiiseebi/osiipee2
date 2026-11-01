using UnityEngine;

// 居酒屋風の飾りを実行時に生成する（すべて当たり判定なし。床・壁の当たり判定はそのまま）
// ・床：濃い木の板張り　・壁：腰板＋土壁　・提灯、暖簾、お品書き　・暖かい照明
public class IzakayaDecor : MonoBehaviour
{
    [Header("床と壁（空なら \"Floor\" / \"Wall\" という名前で探す）")]
    public Renderer floor;
    public Renderer wall;

    [Header("照明")]
    public Color sunColor = new Color(1f, 0.78f, 0.55f);
    public float sunIntensity = 0.9f;
    public Color ambient = new Color(0.32f, 0.2f, 0.13f);
    public Color lanternLight = new Color(1f, 0.55f, 0.25f);
    public float lanternIntensity = 1.2f;

    [Header("配置（ワールド座標）")]
    public float wallFrontZ = 0.9f;    // 壁の手前の面
    public float norenTopY = 1.12f;
    public float lanternX = 0.78f;
    public string norenText = "おしっぴー";
    public string[] menuLeft = { "生ビール", "枝豆", "焼き鳥" };
    public string[] menuRight = { "唐揚げ", "冷奴", "ハイボール" };

    Transform[] norenPanels;

    void Start()
    {
        Lighting();

        if (floor == null) floor = FindRenderer("Floor");
        if (wall == null) wall = FindRenderer("Wall");
        if (floor != null)
            floor.sharedMaterial = ProcTex.Mat(new Color(0.55f, 0.4f, 0.3f), ProcTex.Wood(256, 8, 11f), new Vector2(8f, 8f), 0.25f);
        if (wall != null)
            wall.sharedMaterial = ProcTex.Mat(Color.white, ProcTex.Wall(512), Vector2.one, 0.05f);

        Noren();
        Lantern(-lanternX);
        Lantern(lanternX);
        Menus(menuLeft, -1);
        Menus(menuRight, 1);
    }

    static Renderer FindRenderer(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go.GetComponent<Renderer>() : null;
    }

    void Lighting()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional)
            {
                l.color = sunColor;
                l.intensity = sunIntensity;
            }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
    }

    // ---- 部品 ----
    GameObject Piece(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());   // 飾りは当たり判定なし
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    TextMesh Label(string text, Transform parent, Vector3 pos, float charHeight, Color color)
    {
        var font = GuiScale.Font;
        if (font == null) return null;
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = charHeight / 6.4f;   // 1行の高さ ≒ characterSize × fontSize × 0.1
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.lineSpacing = 0.85f;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    // 暖簾（壁の上部。少しゆれる）
    void Noren()
    {
        var root = new GameObject("Noren").transform;
        root.SetParent(transform, false);
        root.position = new Vector3(0, norenTopY, wallFrontZ - 0.02f);

        var navy = ProcTex.Mat(new Color(0.12f, 0.16f, 0.36f), ProcTex.Cloth(64), Vector2.one, 0.1f);
        var wood = ProcTex.Mat(new Color(0.45f, 0.3f, 0.18f), ProcTex.Wood(128, 2, 3f), Vector2.one, 0.3f);

        int n = Mathf.Max(1, norenText.Length);
        float pw = 0.22f, gap = 0.012f, len = 0.3f;
        float total = n * pw + (n - 1) * gap;
        Piece(PrimitiveType.Cube, "Rod", root, new Vector3(0, 0.01f, 0), new Vector3(total + 0.12f, 0.02f, 0.02f), wood);

        norenPanels = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            var pivot = new GameObject("Panel").transform;   // 上端を軸にゆらす
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(-total / 2 + pw / 2 + i * (pw + gap), 0, 0);
            Piece(PrimitiveType.Cube, "Cloth", pivot, new Vector3(0, -len / 2, 0), new Vector3(pw, len, 0.004f), navy);
            Label(norenText[i].ToString(), pivot, new Vector3(0, -len * 0.45f, -0.004f), 0.13f, new Color(0.97f, 0.95f, 0.9f));
            norenPanels[i] = pivot;
        }
    }

    // 赤い提灯（前に小さな点光源）
    void Lantern(float x)
    {
        var root = new GameObject("Lantern").transform;
        root.SetParent(transform, false);
        root.position = new Vector3(x, 0.95f, wallFrontZ - 0.1f);

        var red = ProcTex.Mat(new Color(0.95f, 0.18f, 0.1f), ProcTex.Lantern(64), Vector2.one, 0.35f);
        var black = ProcTex.Mat(new Color(0.08f, 0.06f, 0.05f), null, Vector2.one, 0.4f);

        Piece(PrimitiveType.Sphere, "Body", root, Vector3.zero, new Vector3(0.15f, 0.2f, 0.15f), red);
        Piece(PrimitiveType.Cylinder, "CapTop", root, new Vector3(0, 0.1f, 0), new Vector3(0.08f, 0.012f, 0.08f), black);
        Piece(PrimitiveType.Cylinder, "CapBottom", root, new Vector3(0, -0.1f, 0), new Vector3(0.08f, 0.012f, 0.08f), black);
        Piece(PrimitiveType.Cube, "String", root, new Vector3(0, 0.25f, 0), new Vector3(0.006f, 0.3f, 0.006f), black);
        Label("酒", root, new Vector3(0, 0, -0.077f), 0.08f, new Color(0.1f, 0.05f, 0.03f));

        var lgo = new GameObject("LanternLight");
        lgo.transform.SetParent(root, false);
        lgo.transform.localPosition = new Vector3(0, 0, -0.2f);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = lanternLight;
        l.intensity = lanternIntensity;
        l.range = 0.9f;
        l.shadows = LightShadows.None;
    }

    // お品書き（縦長の札を壁に並べる）
    void Menus(string[] items, int side)
    {
        if (items == null) return;
        var paper = ProcTex.Mat(new Color(0.95f, 0.9f, 0.76f), ProcTex.Wood(128, 1, 7f), Vector2.one, 0.1f);
        for (int i = 0; i < items.Length; i++)
        {
            float x = side * (0.95f + i * 0.15f);
            var board = new GameObject("Menu").transform;
            board.SetParent(transform, false);
            board.position = new Vector3(x, 0.82f, wallFrontZ - 0.006f);
            Piece(PrimitiveType.Cube, "Board", board, Vector3.zero, new Vector3(0.11f, 0.3f, 0.01f), paper);
            // 縦書き（1文字ずつ改行）
            string vertical = string.Join("\n", items[i].ToCharArray());
            float h = Mathf.Min(0.055f, 0.26f / Mathf.Max(1, items[i].Length));
            Color c = i == 0 && side < 0 ? new Color(0.75f, 0.1f, 0.05f) : new Color(0.1f, 0.07f, 0.05f);
            Label(vertical, board, new Vector3(0, 0, -0.006f), h, c);
        }
    }

    void Update()
    {
        if (norenPanels == null) return;
        for (int i = 0; i < norenPanels.Length; i++)
            norenPanels[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f + i * 0.7f) * 3f, 0, 0);
    }
}

// Perlin ノイズなどでテクスチャを作る（起動時に1回だけ。マテリアルも使い回す）
public static class ProcTex
{
    static Material baseLit;

    // 実行時に作るマテリアルのテンプレート（Assets/Resources/RuntimeLit.mat、シェーダーは URP/Lit）
    // Resources に置いたマテリアルは必ずビルドに入るので、Lit シェーダーと必要なバリアントも残る。
    // ※ CreatePrimitive の箱に付いている標準マテリアルはビルドでは URP 用にならずマゼンタになるため使わない
    public static Material BaseLit
    {
        get
        {
            if (baseLit != null) return baseLit;
            baseLit = Resources.Load<Material>("RuntimeLit");
            if (baseLit == null)
            {
                Debug.LogError("Assets/Resources/RuntimeLit.mat（URP/Lit のマテリアル）が見つかりません。ビルドでマゼンタになります。");
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);   // エディタでだけ動く代わり
                baseLit = tmp.GetComponent<Renderer>().sharedMaterial;
                Object.DestroyImmediate(tmp);
            }
            return baseLit;
        }
    }

    // URP の標準 Lit マテリアルをもとに、色・テクスチャ・ツヤを変えたものを作る
    public static Material Mat(Color color, Texture2D tex, Vector2 tiling, float smoothness)
    {
        var m = new Material(BaseLit);
        m.color = color;
        if (tex != null)
        {
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    static Texture2D New(int w, int h, string name) =>
        new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = name, anisoLevel = 2 };

    static float Fbm(float x, float y)
    {
        return Mathf.PerlinNoise(x, y) * 0.6f + Mathf.PerlinNoise(x * 2.1f, y * 2.1f) * 0.3f
             + Mathf.PerlinNoise(x * 4.3f, y * 4.3f) * 0.1f;
    }

    // 横向きの板張り（白〜茶のグレースケール。色はマテリアルで付ける）
    public static Texture2D Wood(int size, int planks, float seed)
    {
        var t = New(size, size, "Wood");
        var px = new Color[size * size];
        float ph = (float)size / planks;
        for (int y = 0; y < size; y++)
        {
            int row = Mathf.FloorToInt(y / ph);
            float inRow = (y % ph) / ph;
            float tone = 0.8f + 0.2f * Mathf.PerlinNoise(row * 3.7f + seed, seed);   // 板ごとの色の違い
            float joint = Mathf.PerlinNoise(row * 5.1f, seed * 2f) * size;           // 板の継ぎ目の位置
            for (int x = 0; x < size; x++)
            {
                float grain = Fbm(x * 0.012f + seed, y * 0.35f + row * 10f);          // 横に流れる木目
                float rings = 0.5f + 0.5f * Mathf.Sin((grain * 8f + y * 0.15f) * Mathf.PI);
                float v = tone * (0.72f + 0.2f * grain + 0.08f * rings);
                if (inRow < 0.04f || inRow > 0.96f) v *= 0.45f;                       // 板と板のすき間
                if (Mathf.Abs(x - joint) < 1.2f) v *= 0.55f;                          // 板の継ぎ目
                px[y * size + x] = new Color(v, v, v, 1f);
            }
        }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // 壁：下3割が縦の腰板（濃い木）、その上に横木、上は土壁（ざらざら）
    public static Texture2D Wall(int size)
    {
        var t = New(size, size, "Wall");
        var px = new Color[size * size];
        var plaster = new Color(0.78f, 0.62f, 0.45f);
        var woodDark = new Color(0.33f, 0.2f, 0.12f);
        var beam = new Color(0.25f, 0.15f, 0.09f);
        int boards = 40;
        float bw = (float)size / boards;
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                Color c;
                if (v < 0.3f)
                {
                    int b = Mathf.FloorToInt(x / bw);
                    float inB = (x % bw) / bw;
                    float g = Fbm(x * 0.3f + b * 7f, y * 0.02f);
                    float k = (0.75f + 0.25f * Mathf.PerlinNoise(b * 3.3f, 1.7f)) * (0.8f + 0.35f * g);
                    if (inB < 0.08f) k *= 0.5f;
                    c = woodDark * k;
                }
                else if (v < 0.33f)
                {
                    c = beam * (0.85f + 0.3f * Fbm(x * 0.05f, y * 0.5f));
                }
                else
                {
                    float n = Fbm(x * 0.05f, y * 0.05f);
                    float speck = Mathf.PerlinNoise(x * 0.9f, y * 0.9f) > 0.82f ? 0.85f : 1f;   // わらすさの粒
                    c = plaster * (0.82f + 0.25f * n) * speck;
                }
                c.a = 1f;
                px[y * size + x] = c;
            }
        }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // 布のざらつき
    public static Texture2D Cloth(int size)
    {
        var t = New(size, size, "Cloth");
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = 0.85f + 0.15f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
                if ((x + y) % 2 == 0) v *= 0.96f;
                px[y * size + x] = new Color(v, v, v, 1f);
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // 提灯の横じま（骨）
    public static Texture2D Lantern(int size)
    {
        var t = New(size, size, "Lantern");
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float rib = (y % 8) < 1 ? 0.6f : 1f;
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color(rib, rib, rib, 1f);
        }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // 回転寿司のベルト（流れる向きに並ぶ板。色はマテリアルで付ける）
    public static Texture2D Belt(int size)
    {
        var t = New(size, size, "Belt");
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float seg = (u * 2f) % 1f;                    // 1枚のテクスチャに板2枚
                float v = 0.8f + 0.2f * Mathf.Sin(seg * Mathf.PI);
                if (seg < 0.06f) v = 0.35f;                   // 板のすき間
                px[y * size + x] = new Color(v, v, v, 1f);
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }
}
