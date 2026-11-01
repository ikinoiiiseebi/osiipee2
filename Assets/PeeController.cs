using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(ParticleSystem))]
public class PeeController : MonoBehaviour
{
    enum State { Countdown, Peeing, Finishing, Result }

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
    State state = State.Countdown;
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
                }
                break;

            case State.Result:
                if (kb.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                // Shift + Delete で本日のランキングを消去（F12はブラウザの開発者ツールと重なるため）
                if (kb.shiftKey.isPressed && kb.deleteKey.wasPressedThisFrame)
                {
                    PlayerPrefs.DeleteKey(RankingKey);
                    ranking.Clear();
                    myRank = -1;
                }
                break;
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

        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(rect, pieFull);
        GUI.color = new Color(1f, 0.55f, 0.05f, alpha);
        GUI.DrawTexture(rect, pieTex);
        GUI.color = old;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(1f, 0.6f, 0.1f);
        GUI.Label(new Rect(gx - 30, gy + PieSize, PieSize + 60, 36), label, st);
    }

    void OnGUI()
    {
        // 1280x720 を基準に、画面の高さに合わせて文字も位置もまとめて拡大縮小する
        GuiScale.Apply();
        float sw = GuiScale.Width, sh = GuiScale.Height;

        var big = new GUIStyle(GUI.skin.label) { fontSize = 80, alignment = TextAnchor.MiddleCenter };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
        var hud = new GUIStyle(GUI.skin.label) { fontSize = 32 };
        var board = new GUIStyle(GUI.skin.box) { fontSize = 30, alignment = TextAnchor.UpperLeft, padding = new RectOffset(20, 20, 16, 16) };
        Rect center = new Rect(0, sh * 0.2f, sw, 120);
        Rect below = new Rect(0, sh * 0.2f + 120, sw, 460);
        var small = new GUIStyle(mid) { fontSize = 26 };

        switch (state)
        {
            case State.Countdown:
                GUI.Label(center, Mathf.CeilToInt(countdown).ToString(), big);
                string best = ranking.Count > 0 ? $"\n本日のベスト {ranking[0]}点" : "";
                GUI.Label(below,
                    "マウスで狙え（上下で奥のレーン）　左クリック長押しで踏ん張れ\n" +
                    "スペース長押しで我慢（最大1秒）\n" +
                    "ジョッキを9割まで満たせば成功！\n" +
                    $"点数 ＝ 成功1個 {successPoint}点 ＋ 命中率1% {ratePoint}点" + best, mid);
                break;

            case State.Peeing:
            case State.Finishing:
                GUI.Label(new Rect(20, 20, 1600, 50),
                    $"{CalcScore()}点　成功 {Succeeded}/{Finished}　命中率 {Rate():F1}%　残量 {Mathf.Max(0, remaining):F0}%", hud);
                if (state == State.Peeing) DrawHoldGauge();
                if (state == State.Peeing && remaining < weakThreshold)
                    GUI.Label(below, "右クリック連打で振れ！", mid);
                break;

            case State.Result:
                float r = Rate();
                float sr = SuccessRate();
                GUI.Label(center, $"ランク {Rank(sr)}", big);
                string record = myRank == 0 ? "　★ 本日の新記録！ ★" : "";
                GUI.Label(below,
                    $"{finalScore} 点{record}\n" +
                    $"成功ジョッキ {Succeeded}個 × {successPoint}点 ＝ {SuccessScore}点\n" +
                    $"命中率 {r:F1}% × {ratePoint}点 ＝ {RateScore}点\n" +
                    $"あふれ {overflowCount}滴　あなたの足元に {outCount} 滴\n\nRでもう一度", mid);
                GUI.Label(new Rect(0, sh - 60, sw, 40),
                    $"ランクは成功率で決まる（今回 {Succeeded}/{Finished}個 = {sr:F0}%）　S 90%以上 / A 70%以上 / B 50%以上", small);

                string list = "本日のランキング\n";
                for (int i = 0; i < ranking.Count; i++)
                    list += $"{(i == myRank ? "▶" : "　")}{i + 1}位　{ranking[i]}点\n";
                GUI.Box(new Rect(sw - 380, 20, 360, 60 + 44 * rankingSize), list, board);
                break;
        }
    }
}

// OnGUI を 1280x720 基準で描き、実際の画面の高さに合わせて拡大縮小する
public static class GuiScale
{
    public const float RefHeight = 720f;

    public static float Scale => Mathf.Max(0.01f, Screen.height / RefHeight);
    public static float Width => Screen.width / Scale;   // 基準座標での画面の幅
    public static float Height => RefHeight;

    // 画面の文字に使うフォント（PeeController の Inspector で設定）
    public static Font Font;

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
