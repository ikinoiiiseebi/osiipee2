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
