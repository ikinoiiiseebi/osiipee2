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
            GUI.color = Ui.Border;                                  // オレンジのふち
            GUI.DrawTexture(new Rect(rect.x - 4, rect.y - 4, PieSize + 8, PieSize + 8), pieFull);
            GUI.color = Ui.Panel;                                   // こげ茶の土台
            GUI.DrawTexture(rect, pieFull);
            GUI.color = new Color(Ui.Border.r, Ui.Border.g, Ui.Border.b, alpha); // 残り（オレンジ）
            GUI.DrawTexture(new Rect(rect.x + 6, rect.y + 6, PieSize - 12, PieSize - 12), pieTex);
            GUI.color = old;
        }
        Ui.Text(rect, "我慢", 24, Ui.Cream, TextAnchor.MiddleCenter, 2f);
        Ui.Text(new Rect(gx - 40, gy + PieSize + 4, PieSize + 80, 36), label, 24, Ui.Border, TextAnchor.MiddleCenter, 2f);
    }

    // ---- タイトル画面 ----
    void DrawTitle(float sw, float sh)
    {
        Ui.Dim(0.45f);

        // ロゴ（ゆらゆら）
        float bob = Mathf.Sin(Time.time * 2f) * 6f;
        Ui.Outlined(new Rect(0, 40 + bob, sw, 140), "おしっぴー", 116, Ui.Beer, Ui.Cream, TextAnchor.MiddleCenter, 5f);
        Ui.Text(new Rect(0, 178, sw, 44), "ジョッキにビールを注げ！ 3レーンのベルトコンベア", 28, Ui.Cream, TextAnchor.MiddleCenter);

        float gap = 30f;
        float panelW = Mathf.Min(560f, (sw - gap * 3) / 2f);
        float top = 250f, panelH = 330f;

        // あそびかた
        var how = new Rect(sw / 2 - gap / 2 - panelW, top, panelW, panelH);
        Ui.Box(how);
        Ui.Text(new Rect(how.x + 28, how.y + 16, how.width - 56, 44), "あそびかた", 32, Ui.Beer);
        string[] lines =
        {
            "マウスで狙う（上下で奥のレーン）",
            "左クリック長押しで踏ん張る（遠くへ）",
            "スペース長押しで我慢（最大1秒）",
            "終わりかけは右クリック連打で振る",
            "ジョッキを9割まで満たせば成功！",
            $"成功1個 {successPoint}点 ＋ 命中率1% {ratePoint}点",
        };
        for (int i = 0; i < lines.Length; i++)
            Ui.Text(new Rect(how.x + 28, how.y + 70 + i * 40, how.width - 56, 38), "・" + lines[i], 23, Ui.Cream, TextAnchor.MiddleLeft, 2f);

        // 本日のランキング
        var rank = new Rect(sw / 2 + gap / 2, top, panelW, panelH);
        DrawRankingPanel(rank, -1);

        // スタート
        var c = Ui.Beer; c.a = Ui.Blink();
        Ui.Text(new Rect(0, 610, sw, 60), "クリック または スペースでスタート", 36, c, TextAnchor.MiddleCenter);
    }

    // 本日のランキングのパネル（highlight 番目を強調）
    void DrawRankingPanel(Rect r, int highlight)
    {
        Ui.Box(r);
        Ui.Text(new Rect(r.x + 28, r.y + 16, r.width - 56, 44), "本日のランキング", 32, Ui.Beer);
        if (ranking.Count == 0)
        {
            Ui.Text(new Rect(r.x + 28, r.y + 80, r.width - 56, 40), "まだ記録がありません", 24, Ui.Cream, TextAnchor.MiddleLeft, 2f);
            return;
        }
        float rowH = Mathf.Min(48f, (r.height - 90f) / Mathf.Max(1, rankingSize));
        for (int i = 0; i < ranking.Count; i++)
        {
            var row = new Rect(r.x + 18, r.y + 72 + i * rowH, r.width - 36, rowH - 6);
            if (i == highlight) Ui.Round(row, new Color(Ui.Border.r, Ui.Border.g, Ui.Border.b, 0.55f));
            Color col = i == 0 ? Ui.Beer : Ui.Cream;
            Ui.Text(new Rect(row.x + 12, row.y, 90, row.height), $"{i + 1}位", 26, col);
            Ui.Text(new Rect(row.x, row.y, row.width - 14, row.height), $"{ranking[i]:N0} 点", 28, col, TextAnchor.MiddleRight);
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
        Ui.Outlined(new Rect(0, 160, sw, 180), n.ToString(), 160, Ui.Beer, Ui.Cream, TextAnchor.MiddleCenter, 5f);
        Ui.EndScale(m);
        Ui.Text(new Rect(0, 360, sw, 50), "ジョッキを9割まで満たせ！", 34, Ui.Cream, TextAnchor.MiddleCenter);
    }

    // ---- プレイ中の HUD ----
    int lastSucceeded;
    float bumpTime = -10f;

    void DrawHud(float sw)
    {
        if (Succeeded > lastSucceeded) { lastSucceeded = Succeeded; bumpTime = Time.time; }
        float bt = Time.time - bumpTime;

        // 左上：スコア（成功すると跳ねて「+1000」が浮かぶ）
        var sp = new Rect(20, 16, 320, 104);
        Ui.Box(sp);
        Ui.Text(new Rect(sp.x + 22, sp.y + 8, 200, 30), "SCORE", 20, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        float s = bt < 0.35f ? 1f + 0.3f * Mathf.Sin(bt / 0.35f * Mathf.PI) : 1f;
        var m = Ui.BeginScale(new Vector2(sp.x + 110, sp.y + 70), s);
        Ui.Text(new Rect(sp.x + 22, sp.y + 34, sp.width - 44, 64), $"{CalcScore():N0}", 54, Ui.Beer);
        Ui.EndScale(m);
        if (bt < 1f)
        {
            var g = Ui.Border; g.a = 1f - bt;
            Ui.Text(new Rect(sp.xMax + 14, sp.y + 30 - bt * 30f, 200, 50), $"+{successPoint:N0}", 36, g);
        }

        // 右上：成功・命中率・残量
        var st = new Rect(sw - 360, 16, 340, 160);
        Ui.Box(st);
        float lx = st.x + 24, rw = st.width - 48;
        Ui.Text(new Rect(lx, st.y + 12, rw, 44), "成功", 24, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        Ui.Text(new Rect(lx, st.y + 10, rw, 44), $"{Succeeded} / {Finished}", 34, Ui.Beer, TextAnchor.MiddleRight);
        Ui.Text(new Rect(lx, st.y + 58, rw, 44), "命中率", 24, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        Ui.Text(new Rect(lx, st.y + 56, rw, 44), $"{Rate():F1}%", 34, Ui.Beer, TextAnchor.MiddleRight);
        Ui.Text(new Rect(lx, st.y + 104, rw, 44), "残量", 24, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        float tankRatio = Mathf.Max(0f, remaining) / tank;
        Color barCol = Ui.Beer;
        if (remaining < weakThreshold) { barCol = Ui.Alert; barCol.a = Ui.Blink(8f); }
        Ui.Bar(new Rect(lx + 80, st.y + 112, rw - 80, 28), tankRatio, barCol);
    }

    // ---- リザルト ----
    void DrawResult(float sw)
    {
        Ui.Dim(0.5f);
        float t = Time.time - resultTime;

        float mainW = 640f, rankW = 400f, gap = 30f;
        float x0 = sw / 2 - (mainW + gap + rankW) / 2;
        var main = new Rect(x0, 50, mainW, 530);
        Ui.Box(main);
        Ui.Text(new Rect(main.x, main.y + 12, main.width, 36), "RESULT", 26, Ui.Border, TextAnchor.MiddleCenter, 2f);

        // ランク（点数のカウントアップのあとにポンと出る）
        float sr = SuccessRate();
        string rank = Rank(sr);
        Ui.Text(new Rect(main.x + 30, main.y + 56, 200, 36), "ランク", 26, Ui.Cream, TextAnchor.MiddleCenter, 2f);
        float rt = t - 1.2f;
        if (rt >= 0f)
        {
            Color rc = rank == "S" ? Ui.Beer : rank == "A" ? Ui.Border : rank == "B" ? Ui.Cream : new Color(0.75f, 0.7f, 0.65f);
            float rs = 1f + 0.8f * Mathf.Max(0f, 1f - rt / 0.25f);
            var m = Ui.BeginScale(new Vector2(main.x + 130, main.y + 170), rs);
            Ui.Outlined(new Rect(main.x + 30, main.y + 90, 200, 160), rank, 150, rc, Ui.Shadow, TextAnchor.MiddleCenter, 4f);
            Ui.EndScale(m);
        }

        // 点数（カウントアップ）
        float k = Mathf.Clamp01(t / 1.2f);
        int shown = Mathf.RoundToInt(finalScore * (1f - (1f - k) * (1f - k)));
        Ui.Text(new Rect(main.x + 250, main.y + 70, 360, 36), "SCORE", 24, Ui.Cream, TextAnchor.MiddleRight, 2f);
        Ui.Text(new Rect(main.x + 230, main.y + 104, 380, 90), $"{shown:N0}", 72, Ui.Beer, TextAnchor.MiddleRight, 4f);
        if (myRank == 0 && rt >= 0f)
        {
            var c = Ui.Border; c.a = Ui.Blink(5f);
            Ui.Text(new Rect(main.x + 230, main.y + 196, 380, 44), "★ 本日の新記録！ ★", 30, c, TextAnchor.MiddleRight);
        }

        // 内訳
        Ui.Round(new Rect(main.x + 30, main.y + 268, main.width - 60, 3), new Color(Ui.Border.r, Ui.Border.g, Ui.Border.b, 0.6f));
        float bx = main.x + 40, bw = main.width - 80;
        Ui.Text(new Rect(bx, main.y + 286, bw, 44), $"成功ジョッキ {Succeeded}個 × {successPoint:N0}点", 26, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        Ui.Text(new Rect(bx, main.y + 286, bw, 44), $"{SuccessScore:N0} 点", 30, Ui.Beer, TextAnchor.MiddleRight);
        Ui.Text(new Rect(bx, main.y + 334, bw, 44), $"命中率 {Rate():F1}% × {ratePoint}点", 26, Ui.Cream, TextAnchor.MiddleLeft, 2f);
        Ui.Text(new Rect(bx, main.y + 334, bw, 44), $"{RateScore:N0} 点", 30, Ui.Beer, TextAnchor.MiddleRight);

        var dim = Ui.Cream; dim.a = 0.8f;
        Ui.Text(new Rect(main.x, main.y + 396, main.width, 36),
            $"あふれ {overflowCount}滴　あなたの足元に {outCount}滴", 22, dim, TextAnchor.MiddleCenter, 2f);
        Ui.Text(new Rect(main.x, main.y + 450, main.width, 56),
            $"ランクは成功率で決まる（今回 {Succeeded}/{Finished}個 = {sr:F0}%）\nS 90%以上　A 70%以上　B 50%以上", 19, dim, TextAnchor.MiddleCenter, 2f);

        // 本日のランキング
        DrawRankingPanel(new Rect(main.xMax + gap, 50, rankW, 530), myRank);

        // ボタン（キーでもクリックでも）
        if (PillButton(new Rect(sw / 2 - 260, 610, 240, 64), "R：もう一度")) Restart(true);
        if (PillButton(new Rect(sw / 2 + 20, 610, 240, 64), "T：タイトルへ")) Restart(false);
    }

    bool PillButton(Rect r, string label)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Ui.Round(new Rect(r.x + 2, r.y + 5, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
        Ui.Round(r, Ui.Border);
        Ui.Round(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), hover ? new Color(0.35f, 0.2f, 0.08f, 0.95f) : Ui.Panel);
        Ui.Text(r, label, 30, Ui.Beer, TextAnchor.MiddleCenter);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }
}
