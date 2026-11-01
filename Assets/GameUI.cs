using UnityEngine;

// OnGUI を 1280x720 基準で描き、実際の画面の高さに合わせて拡大縮小する
public static class GuiScale
{
    public const float RefHeight = 720f;

    // 画面の文字に使うフォント（PeeController の Inspector で設定）
    public static Font Font;

    public static float Scale => Mathf.Max(0.01f, Screen.height / RefHeight);
    public static float Width => Screen.width / Scale;   // 基準座標での画面の幅
    public static float Height => RefHeight;

    public static void Apply()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        if (Font != null) GUI.skin.font = Font;
    }

    // ワールド座標 → 基準座標の GUI 位置（左上原点）
    public static Vector2 WorldToGui(Camera cam, Vector3 world)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        return new Vector2(sp.x / Scale, (Screen.height - sp.y) / Scale);
    }
}

// ビール色の UI 部品（角丸パネル、影付き文字、バー、跳ねる演出）
public static class Ui
{
    // 配色
    public static readonly Color Panel = new Color(0.169f, 0.102f, 0.055f, 0.85f);  // こげ茶 #2B1A0E
    public static readonly Color Border = new Color(0.949f, 0.549f, 0.157f);         // オレンジ #F28C28
    public static readonly Color Cream = new Color(1f, 0.965f, 0.863f);              // 泡 #FFF6DC
    public static readonly Color Beer = new Color(1f, 0.788f, 0.235f);               // ビール #FFC93C
    public static readonly Color Shadow = new Color(0.227f, 0.133f, 0.063f, 0.9f);   // 影 #3A2210
    public static readonly Color Alert = new Color(1f, 0.416f, 0.239f);              // 注意 #FF6A3D

    const int TexSize = 64, Radius = 18;
    static Texture2D roundTex;
    static GUIStyle roundStyle, textStyle;

    static GUIStyle RoundStyle
    {
        get
        {
            if (roundTex == null)
            {
                roundTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[TexSize * TexSize];
                for (int y = 0; y < TexSize; y++)
                    for (int x = 0; x < TexSize; x++)
                    {
                        // 角からの距離で角丸を作る（ふちはなめらかに）
                        float cx = Mathf.Clamp(x + 0.5f, Radius, TexSize - Radius);
                        float cy = Mathf.Clamp(y + 0.5f, Radius, TexSize - Radius);
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                        byte a = (byte)(Mathf.Clamp01(Radius - d + 0.5f) * 255f);
                        px[y * TexSize + x] = new Color32(255, 255, 255, a);
                    }
                roundTex.SetPixels32(px);
                roundTex.Apply();
                roundStyle = null;
            }
            if (roundStyle == null)
            {
                roundStyle = new GUIStyle();
                roundStyle.normal.background = roundTex;
                roundStyle.border = new RectOffset(Radius, Radius, Radius, Radius);
            }
            return roundStyle;
        }
    }

    static readonly System.Collections.Generic.Dictionary<int, GUIStyle> smallStyles =
        new System.Collections.Generic.Dictionary<int, GUIStyle>();

    // 角丸の四角を塗る（小さい四角は角の丸みも小さくする）
    public static void Round(Rect r, Color c)
    {
        if (Event.current.type != EventType.Repaint) return;
        var style = RoundStyle;
        int b = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(r.width, r.height) / 2f), 1, Radius);
        if (b < Radius)
        {
            if (!smallStyles.TryGetValue(b, out style) || style.normal.background != roundTex)
            {
                style = new GUIStyle(RoundStyle) { border = new RectOffset(b, b, b, b) };
                smallStyles[b] = style;
            }
        }
        var old = GUI.color;
        GUI.color = c;
        style.Draw(r, false, false, false, false);
        GUI.color = old;
    }

    // オレンジのふち付きの角丸パネル
    public static void Box(Rect r)
    {
        Round(new Rect(r.x + 3, r.y + 5, r.width, r.height), new Color(0f, 0f, 0f, 0.35f)); // 落ち影
        Round(r, Border);
        Round(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), Panel);
    }

    // 横バー（ratio 0〜1）
    public static void Bar(Rect r, float ratio, Color fill)
    {
        Round(r, new Color(0f, 0f, 0f, 0.5f));
        ratio = Mathf.Clamp01(ratio);
        float w = (r.width - 6) * ratio;
        if (w < 4f) return;
        Round(new Rect(r.x + 3, r.y + 3, Mathf.Max(w, r.height - 6), r.height - 6), fill);
    }

    static GUIStyle TextStyle(int size, TextAnchor align)
    {
        if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
        textStyle.font = GuiScale.Font;
        textStyle.fontSize = size;
        textStyle.alignment = align;
        return textStyle;
    }

    // マウスを重ねたとき（hover など）も同じ色で描く。
    // これをしないと影の文字がホバー色（明るい色）になり、文字が二重に見える
    static void SetColor(GUIStyle st, Color c)
    {
        st.normal.textColor = c;
        st.hover.textColor = c;
        st.active.textColor = c;
        st.focused.textColor = c;
        st.onNormal.textColor = c;
        st.onHover.textColor = c;
        st.onActive.textColor = c;
        st.onFocused.textColor = c;
    }

    // 影付きの文字
    public static void Text(Rect r, string s, int size, Color c,
                            TextAnchor align = TextAnchor.MiddleLeft, float shadow = 3f)
    {
        var st = TextStyle(size, align);
        if (shadow > 0f)
        {
            SetColor(st, new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * c.a));
            GUI.Label(new Rect(r.x + shadow * 0.6f, r.y + shadow, r.width, r.height), s, st);
        }
        SetColor(st, c);
        GUI.Label(r, s, st);
    }

    // ふち取り＋影付きの文字（ロゴやランク用）
    public static void Outlined(Rect r, string s, int size, Color c, Color outline,
                                TextAnchor align = TextAnchor.MiddleCenter, float thick = 4f)
    {
        var st = TextStyle(size, align);
        SetColor(st, new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * c.a));
        GUI.Label(new Rect(r.x + thick * 1.2f, r.y + thick * 2f, r.width, r.height), s, st);
        SetColor(st, new Color(outline.r, outline.g, outline.b, outline.a * c.a));
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            GUI.Label(new Rect(r.x + Mathf.Cos(a) * thick, r.y + Mathf.Sin(a) * thick, r.width, r.height), s, st);
        }
        SetColor(st, c);
        GUI.Label(r, s, st);
    }

    // pivot（基準座標）を中心に拡大して描く。戻り値の行列を EndScale に渡す
    public static Matrix4x4 BeginScale(Vector2 pivot, float s)
    {
        var old = GUI.matrix;
        GUI.matrix = old * Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one)
                         * Matrix4x4.Scale(new Vector3(s, s, 1f))
                         * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        return old;
    }

    public static void EndScale(Matrix4x4 old) => GUI.matrix = old;

    // 画面全体を暗くする
    public static void Dim(float alpha)
    {
        if (Event.current.type != EventType.Repaint) return;
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0, 0, GuiScale.Width, GuiScale.Height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // 点滅（0.35〜1）
    public static float Blink(float speed = 3f) => 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * speed));
}

// 居酒屋のメニュー表風の UI 部品（木の看板・木札、和紙、黒板、判子、値段表の行）
// テクスチャはすべて起動後に1回だけスクリプトで作る
public static class Wa
{
    // 配色
    public static readonly Color Wood = new Color(0.55f, 0.36f, 0.2f);
    public static readonly Color WoodDark = new Color(0.22f, 0.13f, 0.07f);
    public static readonly Color Washi = new Color(0.97f, 0.93f, 0.83f);
    public static readonly Color Ink = new Color(0.1f, 0.08f, 0.07f);          // 墨
    public static readonly Color Red = new Color(0.82f, 0.13f, 0.1f);          // 判子の赤
    public static readonly Color ChalkWhite = new Color(0.96f, 0.95f, 0.88f);
    public static readonly Color InkOrange = new Color(0.85f, 0.4f, 0.1f);     // 和紙の上の点数 #D9661A

    static Texture2D woodTex, washiTex, boardTex, hankoTex;

    static bool Repaint => Event.current.type == EventType.Repaint;

    static void Draw(Rect r, Texture2D t, Color c)
    {
        if (!Repaint) return;
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    static float Fbm(float x, float y) =>
        Mathf.PerlinNoise(x, y) * 0.6f + Mathf.PerlinNoise(x * 2.3f, y * 2.3f) * 0.3f + Mathf.PerlinNoise(x * 5f, y * 5f) * 0.1f;

    // ---- テクスチャ ----
    static Texture2D WoodTex
    {
        get
        {
            if (woodTex != null) return woodTex;
            int w = 256, h = 128;
            woodTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float g = Fbm(x * 0.01f, y * 0.12f);
                    float rings = 0.5f + 0.5f * Mathf.Sin((g * 10f + y * 0.08f) * Mathf.PI);
                    float k = 0.78f + 0.25f * g + 0.1f * rings;
                    px[y * w + x] = new Color(Wood.r * k, Wood.g * k, Wood.b * k, 1f);
                }
            woodTex.SetPixels(px);
            woodTex.Apply();
            return woodTex;
        }
    }

    static Texture2D WashiTex
    {
        get
        {
            if (washiTex != null) return washiTex;
            int s = 256;
            washiTex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float n = 0.94f + 0.06f * Fbm(x * 0.04f, y * 0.04f);
                    var c = Washi * n;
                    // ふちを少しにじませる
                    float e = Mathf.Min(Mathf.Min(x, s - 1 - x), Mathf.Min(y, s - 1 - y));
                    float wob = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 3f;
                    c.a = Mathf.Clamp01((e - wob) / 3f);
                    px[y * s + x] = c;
                }
            // 繊維（短い線）
            var rnd = new System.Random(7);
            for (int i = 0; i < 180; i++)
            {
                float fx = rnd.Next(s), fy = rnd.Next(s);
                float ang = (float)rnd.NextDouble() * Mathf.PI;
                int len = 6 + rnd.Next(14);
                float shade = rnd.NextDouble() < 0.5 ? 0.9f : 1.04f;
                for (int k = 0; k < len; k++)
                {
                    int x = Mathf.RoundToInt(fx + Mathf.Cos(ang) * k), y = Mathf.RoundToInt(fy + Mathf.Sin(ang) * k);
                    if (x < 4 || y < 4 || x >= s - 4 || y >= s - 4) continue;
                    var c = px[y * s + x];
                    px[y * s + x] = new Color(Mathf.Min(1f, c.r * shade), Mathf.Min(1f, c.g * shade), Mathf.Min(1f, c.b * shade), c.a);
                }
            }
            washiTex.SetPixels(px);
            washiTex.Apply();
            return washiTex;
        }
    }

    static Texture2D BoardTex
    {
        get
        {
            if (boardTex != null) return boardTex;
            int w = 256, h = 256;
            boardTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            var baseC = new Color(0.12f, 0.17f, 0.14f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float smudge = Mathf.PerlinNoise(x * 0.012f + 3f, y * 0.03f) * 0.09f;      // 消し跡のムラ
                    float dust = Mathf.PerlinNoise(x * 0.7f, y * 0.7f) > 0.8f ? 0.05f : 0f;     // チョークの粉
                    float k = smudge + dust;
                    px[y * w + x] = new Color(baseC.r + k, baseC.g + k, baseC.b + k, 1f);
                }
            boardTex.SetPixels(px);
            boardTex.Apply();
            return boardTex;
        }
    }

    static Texture2D HankoTex
    {
        get
        {
            if (hankoTex != null) return hankoTex;
            int s = 128;
            hankoTex = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            float c0 = (s - 1) / 2f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c0, c0)) / c0;   // 0〜1
                    float ring = Band(d, 0.86f, 0.99f) + Band(d, 0.74f, 0.79f);
                    float ink = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) > 0.28f ? 1f : 0.35f;   // かすれ
                    px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring) * ink);
                }
            hankoTex.SetPixels(px);
            hankoTex.Apply(true);
            return hankoTex;
        }
    }

    static float Band(float d, float a, float b) =>
        Mathf.Clamp01((d - a) * 60f) * Mathf.Clamp01((b - d) * 60f);

    // ---- パネル ----

    // 木の看板・木札（nails=true で四隅に釘）
    public static void WoodBoard(Rect r, bool nails = true)
    {
        Ui.Round(new Rect(r.x + 3, r.y + 6, r.width, r.height), new Color(0f, 0f, 0f, 0.4f)); // 落ち影
        Ui.Round(r, WoodDark);
        Draw(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), WoodTex, Color.white);
        Ui.Round(new Rect(r.x + 6, r.y + 5, r.width - 12, 3), new Color(1f, 0.9f, 0.7f, 0.25f));  // 上のふちの明るさ
        if (!nails) return;
        foreach (var p in new[] { new Vector2(r.x + 14, r.y + 14), new Vector2(r.xMax - 14, r.y + 14),
                                  new Vector2(r.x + 14, r.yMax - 14), new Vector2(r.xMax - 14, r.yMax - 14) })
        {
            Ui.Round(new Rect(p.x - 4, p.y - 4, 8, 8), WoodDark);
            Ui.Round(new Rect(p.x - 2, p.y - 3, 3, 3), new Color(1f, 1f, 1f, 0.35f));
        }
    }

    // 和紙
    public static void Paper(Rect r)
    {
        Ui.Round(new Rect(r.x + 4, r.y + 6, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
        Draw(r, WashiTex, Color.white);
    }

    // 黒板（木の枠付き）
    public static void Blackboard(Rect r)
    {
        Ui.Round(new Rect(r.x + 4, r.y + 6, r.width, r.height), new Color(0f, 0f, 0f, 0.4f));
        Ui.Round(r, WoodDark);
        Draw(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), WoodTex, Color.white);
        Draw(new Rect(r.x + 12, r.y + 12, r.width - 24, r.height - 24), BoardTex, Color.white);
    }

    // ---- 文字 ----

    // チョークの手書き風（少し傾けて、粉っぽくにじませる）
    public static void Chalk(Rect r, string s, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, float angle = 0f)
    {
        var old = GUI.matrix;
        var pivot = r.center;
        GUI.matrix = old * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, angle), Vector3.one)
                         * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        var blur = c; blur.a *= 0.3f;
        Ui.Text(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, size, blur, align, 0f);
        var main = c; main.a *= 0.92f;
        Ui.Text(r, s, size, main, align, 0f);
        GUI.matrix = old;
    }

    // 値段表の1行：「左の文字 ・・・・・・ 右の文字」
    public static void PriceRow(Rect r, string left, string right, int size, Color leftCol, Color rightCol, Color dotCol)
    {
        var st = new GUIStyle(GUI.skin.label) { font = GuiScale.Font, fontSize = size, wordWrap = false };
        float lw = st.CalcSize(new GUIContent(left)).x;
        float rw = st.CalcSize(new GUIContent(right)).x;
        var dotSt = new GUIStyle(st) { fontSize = Mathf.Max(10, size - 6) };
        float dw = Mathf.Max(1f, dotSt.CalcSize(new GUIContent("・")).x);
        float space = r.width - lw - rw - 16f;
        int n = Mathf.FloorToInt(space / dw);
        Ui.Text(r, left, size, leftCol, TextAnchor.MiddleLeft, 0f);
        Ui.Text(r, right, size, rightCol, TextAnchor.MiddleRight, 0f);
        if (n > 0)
            Ui.Text(new Rect(r.x + lw + 8f, r.y, n * dw, r.height), new string('・', n), dotSt.fontSize, dotCol, TextAnchor.MiddleCenter, 0f);
    }

    // 判子（center を中心に、scale と回転、alpha で描く）
    public static void Hanko(Vector2 center, float size, string letter, float scale, float angle, float alpha)
    {
        if (alpha <= 0f) return;
        var old = GUI.matrix;
        GUI.matrix = old * Matrix4x4.TRS(center, Quaternion.Euler(0, 0, angle), new Vector3(scale, scale, 1f))
                         * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);
        var r = new Rect(center.x - size / 2, center.y - size / 2, size, size);
        var c = Red; c.a = alpha * 0.92f;
        Draw(r, HankoTex, c);
        Ui.Text(new Rect(r.x, r.y + size * 0.02f, size, size), letter, Mathf.RoundToInt(size * 0.58f), c, TextAnchor.MiddleCenter, 0f);
        GUI.matrix = old;
    }
}
