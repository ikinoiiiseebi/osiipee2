using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(ParticleSystem))]
public class PeeController : MonoBehaviour
{
    enum State { Title, Countdown, Peeing, Finishing, Result }

    [Header("狙い（度）")]
    public float yawRange = 30f;
    public float downAngle = 40f;
    public float upAngle = -20f;

    [Header("勢い")]
    public float normalSpeed = 2.5f;
    public float pushSpeed = 4f;     // 左クリック（踏ん張り）
    public float weakSpeed = 0.8f;   // 終わりかけの弱い勢い

    [Header("タンク")]
    public float tank = 100f;
    public float normalDrain = 4f;   // 1秒あたりの減り
    public float pushDrain = 7f;     // 踏ん張り中の減り
    public float weakThreshold = 15f;// 残りがこれ以下で勢いが落ちる
    public float shakeBoostTime = 0.15f; // 右クリック1回で勢いが戻る時間

    [Header("我慢（スペース長押し）")]
    public float holdMax = 1f;       // 連続で我慢できる秒数
    public float holdCooldown = 1f;  // 我慢のあとのクールタイム

    [Header("文字（YuseiMagic-Regular.ttf を入れる。WebGLで日本語を出すのに必須）")]
    public Font uiFont;

    [Header("跳ね返り")]
    public ParticleSystem splash;
    public float splashThreshold = 1.2f;
    public float splashMaxSpeed = 4f;
    public float maxSplashChance = 0.4f;
    public int splashDrops = 3;

    [Header("コンベア（空なら自動で探す）")]
    public BeerConveyor conveyor;

    [Header("スコア ＝ 成功数×successPoint ＋ 命中率(%)×ratePoint")]
    public int successPoint = 1000;   // 成功ジョッキ1個の点
    public int ratePoint = 10;        // 命中率1%あたりの点
    public int rankingSize = 5;

    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
    int inCount, outCount, overflowCount;
    State state = State.Title;
    static bool skipTitle;   // リザルトで R（もう一度）を押したときはタイトルを飛ばす
    float resultTime;
    float countdown = 3f, elapsed, remaining, lastShake = -10f, endTimer;
    bool holding;
    float holdUsed, cooldownLeft;
    int finalScore, myRank = -1;
    List<int> ranking = new List<int>();

    string RankingKey => "ranking_" + System.DateTime.Now.ToString("yyyyMMdd");

    void Awake()
    {
        GuiScale.Font = uiFont;
    }

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        remaining = tank;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ranking = LoadRanking();
        if (conveyor == null) conveyor = FindFirstObjectByType<BeerConveyor>();
        if (GetComponent<PeeLook>() == null) gameObject.AddComponent<PeeLook>(); // 液体っぽい見た目
        state = skipTitle ? State.Countdown : State.Title;
        skipTitle = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // スペースなどのキーをゲームが受け取り、ページがスクロールしないようにする
        WebGLInput.captureAllKeyboardInput = true;
        DisableContextMenu();   // 右クリックメニューを出さない
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern void DisableContextMenu();
#endif

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        float dt = Time.deltaTime;

        // 狙い（マウス位置）
        Vector2 p = mouse.position.ReadValue();
        float nx = Mathf.Clamp01(p.x / Screen.width);
        float ny = Mathf.Clamp01(p.y / Screen.height);
        transform.localRotation = Quaternion.Euler(
            Mathf.Lerp(downAngle, upAngle, ny),
            Mathf.Lerp(-yawRange, yawRange, nx), 0f);

        switch (state)
        {
            case State.Title:
                if (mouse.leftButton.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    state = State.Countdown;
                CheckClearRanking(kb);
                break;

            case State.Countdown:
                countdown -= dt;
                if (countdown <= 0f)
                {
                    state = State.Peeing;
                    ps.Play();
                    if (conveyor != null) conveyor.Begin();
                }
                break;

            case State.Peeing:
                elapsed += dt;
                if (UpdateHold(kb, dt)) break;   // 我慢中は発射もタンクの減りも止まる

                bool push = mouse.leftButton.isPressed;
                if (mouse.rightButton.wasPressedThisFrame) lastShake = Time.time;

                remaining -= (push ? pushDrain : normalDrain) * dt;
                float speed = push ? pushSpeed : normalSpeed;

                if (remaining < weakThreshold)
                {
                    bool shaking = Time.time - lastShake < shakeBoostTime;
                    float t = Mathf.Clamp01(remaining / weakThreshold);
                    speed = shaking ? normalSpeed : Mathf.Lerp(weakSpeed, speed, t);
                }
                var main = ps.main;
                main.startSpeed = speed;

                if (remaining <= 0f)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (conveyor != null) conveyor.StopSpawning();
                    state = State.Finishing;
                    endTimer = 2f;
                }
                break;

            case State.Finishing:
                endTimer -= dt;
                if (endTimer <= 0f)
                {
                    if (conveyor != null) conveyor.Halt();
                    finalScore = CalcScore();
                    SaveScore(finalScore);
                    state = State.Result;
                    resultTime = Time.time;
                }
                break;

            case State.Result:
                if (kb.rKey.wasPressedThisFrame) Restart(true);    // もう一度
                if (kb.tKey.wasPressedThisFrame) Restart(false);   // タイトルへ
                CheckClearRanking(kb);
                break;
        }
    }

    void Restart(bool playAgain)
    {
        skipTitle = playAgain;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Shift + Delete で本日のランキングを消去（F12はブラウザの開発者ツールと重なるため）
    void CheckClearRanking(Keyboard kb)
    {
        if (kb.shiftKey.isPressed && kb.deleteKey.wasPressedThisFrame)
        {
            PlayerPrefs.DeleteKey(RankingKey);
            ranking.Clear();
            myRank = -1;
        }
    }

    // スペース長押しで我慢。我慢中なら true
    bool UpdateHold(Keyboard kb, float dt)
    {
        if (holding)
        {
            holdUsed += dt;
            if (!kb.spaceKey.isPressed || holdUsed >= holdMax)
            {
                holding = false;
                cooldownLeft = holdCooldown;
                ps.Play();
                return false;
            }
            return true;
        }

        if (cooldownLeft > 0f) cooldownLeft -= dt;
        if (cooldownLeft <= 0f && kb.spaceKey.wasPressedThisFrame)
        {
            holding = true;
            holdUsed = 0f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            return true;
        }
        return false;
    }

    void OnParticleCollision(GameObject other)
    {
        int n = ps.GetCollisionEvents(other, events);

        // 液面：注げた（満タンならあふれ＝外れ）
        var surface = other.GetComponent<BeerSurface>();
        if (surface != null && surface.mug != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (surface.mug.AddDrop()) inCount++;
                else overflowCount++;
            }
            return;
        }

        // それ以外（ジョッキのふち・外側、コンベア、床、壁）はすべて外れ
        outCount += n;

        // ジョッキに正面から強く当たると跳ねる
        if (other.GetComponentInParent<BeerMug>() != null)
        {
            for (int i = 0; i < n; i++)
            {
                var e = events[i];
                float vn = Mathf.Abs(Vector3.Dot(e.velocity, e.normal));
                float chance = Mathf.Clamp01(
                    (vn - splashThreshold) / (splashMaxSpeed - splashThreshold)) * maxSplashChance;
                if (Random.value < chance) EmitSplash(e);
            }
        }
    }

    void EmitSplash(ParticleCollisionEvent e)
    {
        if (splash == null) return;
        Vector3 bounce = Vector3.Reflect(e.velocity, e.normal) * 0.4f;
        for (int k = 0; k < splashDrops; k++)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = e.intersection + e.normal * 0.01f,
                velocity = bounce + Random.insideUnitSphere * 0.5f,
                applyShapeToPosition = false
            };
            splash.Emit(ep, 1);
        }
    }

    float Rate()
    {
        int total = inCount + outCount + overflowCount;
        return total == 0 ? 100f : inCount * 100f / total;
    }

    int Succeeded => conveyor != null ? conveyor.Succeeded : 0;
    int Finished => conveyor != null ? conveyor.Finished : 0;

    int SuccessScore => Succeeded * successPoint;
    int RateScore => Mathf.RoundToInt(Rate() * ratePoint);
    int CalcScore() => SuccessScore + RateScore;

    // 成功率（成功 ÷ 判定されたジョッキ）
    float SuccessRate() => Finished == 0 ? 0f : Succeeded * 100f / Finished;
    string Rank(float sr) => sr >= 90f ? "S" : sr >= 70f ? "A" : sr >= 50f ? "B" : "C";

    List<int> LoadRanking()
    {
        string s = PlayerPrefs.GetString(RankingKey, "");
        return s.Length == 0 ? new List<int>() : s.Split(',').Select(int.Parse).ToList();
    }

    void SaveScore(int score)
    {
        ranking.Add(score);
        ranking = ranking.OrderByDescending(x => x).Take(rankingSize).ToList();
        myRank = ranking.IndexOf(score);
        PlayerPrefs.SetString(RankingKey, string.Join(",", ranking));
        PlayerPrefs.Save();
    }

    // ---- 我慢ゲージ（円グラフ。発射位置の右隣に出す） ----
    const int PieSize = 96;
    Texture2D pieFull, pieTex;
    int pieKey = -1;

    // 上から時計回りに ratio 分だけ白く塗った円のテクスチャ
    static void FillPie(Texture2D tex, float ratio)
    {
        var px = new Color32[PieSize * PieSize];
        float c = (PieSize - 1) * 0.5f, rad = PieSize * 0.5f - 1f;
        for (int y = 0; y < PieSize; y++)
            for (int x = 0; x < PieSize; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (ang < 0f) ang += 360f;
                byte a = (byte)(Mathf.Clamp01(rad - d + 0.5f) * 255f); // ふちをなめらかに
                bool inside = ang <= ratio * 360f;
                px[y * PieSize + x] = new Color32(255, 255, 255, inside ? a : (byte)0);
            }
        tex.SetPixels32(px);
        tex.Apply();
    }

    static Texture2D NewPieTex() =>
        new Texture2D(PieSize, PieSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

    void DrawHoldGauge()
    {
        var cam = Camera.main;
        if (cam == null) return;

        float ratio;
        string label;
        float alpha = 1f;
        if (holding) { ratio = 1f - holdUsed / holdMax; label = "我慢中"; }
        else if (cooldownLeft > 0f) { ratio = 1f - cooldownLeft / holdCooldown; label = "まって"; alpha = 0.4f; }
        else { ratio = 1f; label = "我慢OK"; }

        if (pieFull == null) { pieFull = NewPieTex(); FillPie(pieFull, 1f); }
        if (pieTex == null) pieTex = NewPieTex();
        int key = Mathf.RoundToInt(ratio * 100f);
        if (key != pieKey) { pieKey = key; FillPie(pieTex, ratio); }

        // 発射位置の右隣（画面の外なら下端に寄せる）
        Vector2 gp = GuiScale.WorldToGui(cam, transform.position);
        float gx = gp.x + 40f;
        float gy = Mathf.Clamp(gp.y - PieSize * 0.5f, 10f, GuiScale.Height - PieSize - 50f);
        var rect = new Rect(gx, gy, PieSize, PieSize);

        if (Event.current.type == EventType.Repaint)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);              // 落ち影
            GUI.DrawTexture(new Rect(rect.x - 3, rect.y + 2, PieSize + 6, PieSize + 6), pieFull);
            GUI.color = Wa.Wood;                                    // 木のふち
            GUI.DrawTexture(new Rect(rect.x - 4, rect.y - 4, PieSize + 8, PieSize + 8), pieFull);
            GUI.color = Wa.WoodDark;                                // こげ茶の土台
            GUI.DrawTexture(rect, pieFull);
            GUI.color = new Color(Ui.Border.r, Ui.Border.g, Ui.Border.b, alpha); // 残り（オレンジ）
            GUI.DrawTexture(new Rect(rect.x + 6, rect.y + 6, PieSize - 12, PieSize - 12), pieTex);
            GUI.color = old;
        }
        Ui.Text(rect, "我慢", 24, Wa.Washi, TextAnchor.MiddleCenter, 2f);
        Ui.Text(new Rect(gx - 40, gy + PieSize + 4, PieSize + 80, 36), label, 24, Ui.Border, TextAnchor.MiddleCenter, 2f);
    }

    // ---- タイトル画面 ----
    void DrawTitle(float sw, float sh)
    {
        Ui.Dim(0.35f);

        // 木の看板に「おしっぴー」（少しゆれる）
        float bob = Mathf.Sin(Time.time * 2f) * 3f;
        var sign = new Rect(sw / 2 - 330, 26 + bob, 660, 156);
        Wa.WoodBoard(sign);
        Ui.Outlined(new Rect(sign.x, sign.y + 6, sign.width, 112), "おしっぴー", 100, Ui.Beer, Wa.WoodDark, TextAnchor.MiddleCenter, 4f);
        Ui.Text(new Rect(sign.x, sign.y + 112, sign.width, 34), "〜 生ビール 注ぎ放題 〜", 22, Wa.Washi, TextAnchor.MiddleCenter, 2f);

        float gap = 30f;
        float panelW = Mathf.Min(560f, (sw - gap * 3) / 2f);
        float top = 212f, panelH = 372f;

        // 和紙の「本日のおすすめ」（操作説明）
        var how = new Rect(sw / 2 - gap / 2 - panelW, top, panelW, panelH);
        Wa.Paper(how);
        Ui.Text(new Rect(how.x + 28, how.y + 18, how.width - 56, 44), "本日のおすすめ", 32, Wa.Red, TextAnchor.MiddleLeft, 0f);
        Ui.Round(new Rect(how.x + 28, how.y + 64, how.width - 56, 2), new Color(Wa.Red.r, Wa.Red.g, Wa.Red.b, 0.6f));
        string[,] items =
        {
            { "狙い撃ち（マウス）", "上下で奥へ" },
            { "踏ん張り（左クリック長押し）", "遠くへ" },
            { "我慢（スペース長押し）", "最大1秒" },
            { "振り（右クリック連打）", "終わりかけに" },
            { "一杯（9割で成功）", $"{successPoint:N0}点" },
            { "命中率 1%", $"{ratePoint}点" },
        };
        var dot = new Color(Wa.Ink.r, Wa.Ink.g, Wa.Ink.b, 0.45f);
        for (int i = 0; i < items.GetLength(0); i++)
            Wa.PriceRow(new Rect(how.x + 28, how.y + 80 + i * 46, how.width - 56, 40),
                        items[i, 0], items[i, 1], 22, Wa.Ink, Wa.InkOrange, dot);

        // 黒板の「本日のランキング」
        DrawRankingBoard(new Rect(sw / 2 + gap / 2, top, panelW, panelH), -1);

        // 木札の「開店！」
        var start = new Rect(sw / 2 - 260, 610, 520, 72);
        Wa.WoodBoard(start, false);
        var c = Ui.Beer; c.a = Ui.Blink();
        Ui.Text(start, "クリック または スペースで開店！", 30, c, TextAnchor.MiddleCenter);
    }

    // 黒板にチョークで書いたランキング（highlight 番目に黄色の下線）
    void DrawRankingBoard(Rect r, int highlight)
    {
        Wa.Blackboard(r);
        Wa.Chalk(new Rect(r.x + 30, r.y + 22, r.width - 60, 48), "本日のランキング", 32, Wa.ChalkWhite, TextAnchor.MiddleLeft, -1.5f);
        Ui.Round(new Rect(r.x + 30, r.y + 72, r.width - 60, 2), new Color(Wa.ChalkWhite.r, Wa.ChalkWhite.g, Wa.ChalkWhite.b, 0.4f));
        if (ranking.Count == 0)
        {
            Wa.Chalk(new Rect(r.x + 30, r.y + 90, r.width - 60, 40), "まだ記録なし", 24, Wa.ChalkWhite, TextAnchor.MiddleLeft, 1f);
            return;
        }
        float rowH = Mathf.Min(54f, (r.height - 110f) / Mathf.Max(1, rankingSize));
        for (int i = 0; i < ranking.Count; i++)
        {
            var row = new Rect(r.x + 30, r.y + 86 + i * rowH, r.width - 60, rowH - 6);
            float ang = (i % 2 == 0) ? -1f : 0.8f;
            Color col = i == 0 ? Ui.Beer : Wa.ChalkWhite;
            Wa.Chalk(new Rect(row.x, row.y, 100, row.height), $"{i + 1}位", 26, col, TextAnchor.MiddleLeft, ang);
            Wa.Chalk(new Rect(row.x, row.y, row.width, row.height), $"{ranking[i]:N0} 点", 28, col, TextAnchor.MiddleRight, ang);
            if (i == highlight)
                Ui.Round(new Rect(row.x - 4, row.yMax - 4, row.width + 8, 4), new Color(Ui.Beer.r, Ui.Beer.g, Ui.Beer.b, 0.85f));
        }
    }

    void OnGUI()
    {
        // 1280x720 を基準に、画面の高さに合わせて文字も位置もまとめて拡大縮小する
        GuiScale.Apply();
        float sw = GuiScale.Width, sh = GuiScale.Height;

        switch (state)
        {
            case State.Title:
                DrawTitle(sw, sh);
                break;

            case State.Countdown:
                DrawCountdown(sw);
                break;

            case State.Peeing:
            case State.Finishing:
                DrawHud(sw);
                if (state == State.Peeing) DrawHoldGauge();
                if (state == State.Peeing && remaining < weakThreshold)
                {
                    var c = Ui.Alert; c.a = Ui.Blink(8f);
                    var m = Ui.BeginScale(new Vector2(sw / 2, 560), 1f + 0.06f * Mathf.Sin(Time.time * 16f));
                    Ui.Text(new Rect(0, 530, sw, 60), "右クリック連打で振れ！", 44, c, TextAnchor.MiddleCenter);
                    Ui.EndScale(m);
                }
                break;

            case State.Result:
                DrawResult(sw);
                break;
        }
    }

    // ---- カウントダウン ----
    void DrawCountdown(float sw)
    {
        int n = Mathf.CeilToInt(countdown);
        float frac = countdown - Mathf.Floor(countdown);   // 1→0 へ減る
        var m = Ui.BeginScale(new Vector2(sw / 2, 250), 1f + 0.5f * frac * frac);
        Ui.Outlined(new Rect(0, 160, sw, 180), n.ToString(), 160, Ui.Beer, Wa.WoodDark, TextAnchor.MiddleCenter, 5f);
        Ui.EndScale(m);
        var tag = new Rect(sw / 2 - 230, 352, 460, 60);
        Wa.WoodBoard(tag, false);
        Ui.Text(tag, "ジョッキを9割まで満たせ！", 28, Wa.Washi, TextAnchor.MiddleCenter);
    }

    // ---- プレイ中の HUD ----
    int lastSucceeded;
    float bumpTime = -10f;

    void DrawHud(float sw)
    {
        if (Succeeded > lastSucceeded) { lastSucceeded = Succeeded; bumpTime = Time.time; }
        float bt = Time.time - bumpTime;

        // 左上：木札の「お会計」（成功すると跳ねて「+1,000」が浮かぶ）
        var sp = new Rect(20, 16, 320, 104);
        Wa.WoodBoard(sp);
        Ui.Text(new Rect(sp.x + 28, sp.y + 8, 200, 30), "お会計", 20, Wa.Washi, TextAnchor.MiddleLeft, 2f);
        float s = bt < 0.35f ? 1f + 0.3f * Mathf.Sin(bt / 0.35f * Mathf.PI) : 1f;
        var m = Ui.BeginScale(new Vector2(sp.x + 110, sp.y + 70), s);
        Ui.Text(new Rect(sp.x + 28, sp.y + 34, sp.width - 56, 64), $"{CalcScore():N0}点", 50, Ui.Beer);
        Ui.EndScale(m);
        if (bt < 1f)
        {
            var g = Ui.Border; g.a = 1f - bt;
            Ui.Text(new Rect(sp.xMax + 14, sp.y + 30 - bt * 30f, 200, 50), $"+{successPoint:N0}", 36, g);
        }

        // 右上：和紙の値段表（成功・命中率・残量）
        var st = new Rect(sw - 360, 16, 340, 164);
        Wa.Paper(st);
        float lx = st.x + 24, rw = st.width - 48;
        var dot = new Color(Wa.Ink.r, Wa.Ink.g, Wa.Ink.b, 0.45f);
        Wa.PriceRow(new Rect(lx, st.y + 12, rw, 44), "成功", $"{Succeeded} / {Finished} 杯", 26, Wa.Ink, Wa.InkOrange, dot);
        Wa.PriceRow(new Rect(lx, st.y + 58, rw, 44), "命中率", $"{Rate():F1}%", 26, Wa.Ink, Wa.InkOrange, dot);
        Ui.Text(new Rect(lx, st.y + 106, rw, 44), "残量", 26, Wa.Ink, TextAnchor.MiddleLeft, 0f);
        float tankRatio = Mathf.Max(0f, remaining) / tank;
        Color barCol = Ui.Beer;
        if (remaining < weakThreshold) { barCol = Ui.Alert; barCol.a = Ui.Blink(8f); }
        Ui.Bar(new Rect(lx + 80, st.y + 114, rw - 80, 28), tankRatio, barCol);
    }

    // ---- リザルト ----
    void DrawResult(float sw)
    {
        Ui.Dim(0.45f);
        float t = Time.time - resultTime;
        float stampT = t - 1.2f;                       // 判子を押す時刻からの経過

        float mainW = 640f, rankW = 400f, gap = 30f;
        float x0 = sw / 2 - (mainW + gap + rankW) / 2;

        // 判子を押した瞬間に伝票が少しゆれる
        Vector2 shake = Vector2.zero;
        if (stampT >= 0f && stampT < 0.25f)
        {
            float a = 5f * (1f - stampT / 0.25f);
            shake = new Vector2(Mathf.Sin(stampT * 90f) * a, Mathf.Cos(stampT * 70f) * a);
        }
        var main = new Rect(x0 + shake.x, 36 + shake.y, mainW, 548);
        Wa.Paper(main);

        // 見出し「御会計」と二重線
        Ui.Text(new Rect(main.x, main.y + 16, main.width, 56), "御 会 計", 40, Wa.Ink, TextAnchor.MiddleCenter, 0f);
        var line = new Color(Wa.Ink.r, Wa.Ink.g, Wa.Ink.b, 0.7f);
        Ui.Round(new Rect(main.x + 40, main.y + 78, main.width - 80, 2), line);
        Ui.Round(new Rect(main.x + 40, main.y + 84, main.width - 80, 2), line);

        // 値段表（内訳）
        var dot = new Color(Wa.Ink.r, Wa.Ink.g, Wa.Ink.b, 0.45f);
        float bx = main.x + 44, bw = main.width - 88;
        Wa.PriceRow(new Rect(bx, main.y + 104, bw, 46), $"成功ジョッキ ×{Succeeded}", $"{SuccessScore:N0}点", 28, Wa.Ink, Wa.InkOrange, dot);
        Wa.PriceRow(new Rect(bx, main.y + 154, bw, 46), $"命中率 {Rate():F1}%", $"{RateScore:N0}点", 28, Wa.Ink, Wa.InkOrange, dot);
        Ui.Round(new Rect(bx, main.y + 212, bw, 2), line);

        // 合計（カウントアップ）
        float k = Mathf.Clamp01(t / 1.2f);
        int shown = Mathf.RoundToInt(finalScore * (1f - (1f - k) * (1f - k)));
        Wa.PriceRow(new Rect(bx, main.y + 226, bw, 64), "合計", $"{shown:N0}点", 42, Wa.Ink, Wa.InkOrange, dot);

        var dim = new Color(Wa.Ink.r, Wa.Ink.g, Wa.Ink.b, 0.75f);
        float sr = SuccessRate();
        Ui.Text(new Rect(bx, main.y + 306, bw, 32), $"あふれ {overflowCount}滴　あなたの足元に {outCount}滴", 20, dim, TextAnchor.MiddleLeft, 0f);
        Ui.Text(new Rect(bx, main.y + 346, bw, 30), $"判定は成功率（今回 {Succeeded}/{Finished}杯 ＝ {sr:F0}%）", 19, dim, TextAnchor.MiddleLeft, 0f);
        Ui.Text(new Rect(bx, main.y + 376, bw, 30), "S 90%以上　A 70%以上　B 50%以上", 19, dim, TextAnchor.MiddleLeft, 0f);

        // 新記録
        if (myRank == 0 && stampT >= 0f)
        {
            var c = Wa.Red; c.a = Ui.Blink(5f);
            Ui.Text(new Rect(bx, main.y + 440, 300, 56), "本日一番！", 40, c, TextAnchor.MiddleLeft, 0f);
        }

        // 赤い判子がポンと押される（大きく出て、縮みながら斜めに着地）
        if (stampT >= 0f)
        {
            float sc = stampT < 0.12f ? Mathf.Lerp(2.4f, 0.92f, stampT / 0.12f)
                     : stampT < 0.22f ? Mathf.Lerp(0.92f, 1f, (stampT - 0.12f) / 0.1f) : 1f;
            float al = Mathf.Clamp01(stampT / 0.08f);
            Wa.Hanko(new Vector2(main.xMax - 130, main.y + 440), 170f, Rank(sr), sc, -12f, al);
        }

        // 黒板のランキング（自分の順位に黄色の下線）
        DrawRankingBoard(new Rect(x0 + mainW + gap, 36, rankW, 548), myRank);

        // 木札のボタン（キーでもクリックでも）
        if (TagButton(new Rect(sw / 2 - 260, 606, 240, 64), "R：もう一度")) Restart(true);
        if (TagButton(new Rect(sw / 2 + 20, 606, 240, 64), "T：タイトルへ")) Restart(false);
    }

    bool TagButton(Rect r, string label)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Wa.WoodBoard(r, false);
        if (hover) Ui.Round(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), new Color(1f, 0.9f, 0.6f, 0.15f));
        Ui.Text(r, label, 30, Ui.Beer, TextAnchor.MiddleCenter);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }
}
