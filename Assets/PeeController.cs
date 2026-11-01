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
    public float normalDrain = 4f;   // 1秒あたりの減り（約25秒で空）
    public float pushDrain = 7f;     // 踏ん張り中の減り
    public float weakThreshold = 15f;// 残りがこれ以下で勢いが落ちる
    public float shakeBoostTime = 0.15f; // スペース1回で勢いが戻る時間

    [Header("跳ね返り")]
    public ParticleSystem splash;
    public float splashThreshold = 1.2f;
    public float splashMaxSpeed = 4f;
    public float maxSplashChance = 0.4f;
    public int splashDrops = 3;

    [Header("スコア")]
    public float flyBonus = 3000f;        // ハエ命中率100%で加算される点
    public float parTime = 30f;           // これより早いとボーナス
    public float timeBonusPerSec = 50f;   // 1秒早いごとの加算
    public int rankingSize = 5;

    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
    int inCount, outCount, overflowCount, flyCount;
    State state = State.Countdown;
    float countdown = 3f, elapsed, remaining, lastShake = -10f, endTimer;
    int finalScore, myRank = -1;
    List<int> ranking = new List<int>();

    string RankingKey => "ranking_" + System.DateTime.Now.ToString("yyyyMMdd");

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        remaining = tank;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ranking = LoadRanking();
    }

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
                if (countdown <= 0f) { state = State.Peeing; ps.Play(); }
                break;

            case State.Peeing:
                elapsed += dt;
                bool push = mouse.leftButton.isPressed;
                if (kb.spaceKey.wasPressedThisFrame) lastShake = Time.time;

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
                    state = State.Finishing;
                    endTimer = 2f;
                }
                break;

            case State.Finishing:
                endTimer -= dt;
                if (endTimer <= 0f)
                {
                    finalScore = CalcScore();
                    SaveScore(finalScore);
                    state = State.Result;
                }
                break;

            case State.Result:
                if (kb.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                if (kb.f12Key.wasPressedThisFrame)   // 本日のランキングを消去（テスト用）
                {
                    PlayerPrefs.DeleteKey(RankingKey);
                    ranking.Clear();
                    myRank = -1;
                }
                break;
        }
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

    float FlyRate() => inCount == 0 ? 0f : flyCount * 100f / inCount;

    int CalcScore()
    {
        float score = Rate() * 100f
                    + FlyRate() / 100f * flyBonus
                    + Mathf.Max(0f, parTime - elapsed) * timeBonusPerSec;
        return Mathf.RoundToInt(score);
    }

    string Rank(float r) => r >= 98f ? "S" : r >= 95f ? "A" : r >= 90f ? "B" : "C";

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

    void OnGUI()
    {
        var big = new GUIStyle(GUI.skin.label) { fontSize = 80, alignment = TextAnchor.MiddleCenter };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
        var hud = new GUIStyle(GUI.skin.label) { fontSize = 32 };
        var board = new GUIStyle(GUI.skin.box) { fontSize = 30, alignment = TextAnchor.UpperLeft, padding = new RectOffset(20, 20, 16, 16) };
        Rect center = new Rect(0, Screen.height * 0.2f, Screen.width, 120);
        Rect below = new Rect(0, Screen.height * 0.2f + 120, Screen.width, 300);

        switch (state)
        {
            case State.Countdown:
                GUI.Label(center, Mathf.CeilToInt(countdown).ToString(), big);
                string best = ranking.Count > 0 ? $"\n本日のベスト {ranking[0]}点" : "";
                GUI.Label(below, "マウスで狙え　左クリックで踏ん張れ\nハエを狙うと高得点" + best, mid);
                break;

            case State.Peeing:
            case State.Finishing:
                GUI.Label(new Rect(20, 20, 1200, 50),
                    $"枠内率 {Rate():F1}%　残量 {Mathf.Max(0, remaining):F0}%　{elapsed:F1}秒　あふれ {overflowCount}", hud);
                if (state == State.Peeing && remaining < weakThreshold)
                    GUI.Label(below, "スペース連打で振れ！", mid);
                break;

            case State.Result:
                float r = Rate();
                GUI.Label(center, $"ランク {Rank(r)}", big);
                string record = myRank == 0 ? "\n★ 本日の新記録！ ★" : "";
                GUI.Label(below,
                    $"{finalScore} 点{record}\n" +
                    $"枠内率 {r:F1}%　タイム {elapsed:F1}秒　ハエ命中 {FlyRate():F1}%\n" +
                    $"あなたの足元に {outCount} 滴\n\nRでもう一度", mid);

                string list = "本日のランキング\n";
                for (int i = 0; i < ranking.Count; i++)
                    list += $"{(i == myRank ? "▶" : "　")}{i + 1}位　{ranking[i]}点\n";
                GUI.Box(new Rect(Screen.width - 380, 20, 360, 60 + 44 * rankingSize), list, board);
                break;
        }
    }
}