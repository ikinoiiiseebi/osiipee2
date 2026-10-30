using System.Collections.Generic;
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

    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
    int inCount, outCount;
    State state = State.Countdown;
    float countdown = 3f, elapsed, remaining, lastShake = -10f, endTimer;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        remaining = tank;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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

                // 終わりかけ：勢いが落ちる。スペース（振る）で一瞬戻る
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
                    endTimer = 2f; // 空中の粒が落ち切るのを待つ
                }
                break;

            case State.Finishing:
                endTimer -= dt;
                if (endTimer <= 0f) state = State.Result;
                break;

            case State.Result:
                if (kb.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                break;
        }
    }

    void OnParticleCollision(GameObject other)
    {
        int n = ps.GetCollisionEvents(other, events);
        if (other.CompareTag("Urinal")) inCount += n;
        else if (other.CompareTag("Out")) outCount += n;
    }

    float Rate()
    {
        int total = inCount + outCount;
        return total == 0 ? 100f : inCount * 100f / total;
    }

    string Rank(float r) => r >= 98f ? "S" : r >= 95f ? "A" : r >= 90f ? "B" : "C";

    void OnGUI()
    {
        var big = new GUIStyle(GUI.skin.label) { fontSize = 80, alignment = TextAnchor.MiddleCenter };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
        var hud = new GUIStyle(GUI.skin.label) { fontSize = 32 };
        Rect center = new Rect(0, Screen.height * 0.25f, Screen.width, 120);
        Rect below = new Rect(0, Screen.height * 0.25f + 120, Screen.width, 200);

        switch (state)
        {
            case State.Countdown:
                GUI.Label(center, Mathf.CeilToInt(countdown).ToString(), big);
                GUI.Label(below, "マウスで狙え　左クリックで踏ん張れ", mid);
                break;

            case State.Peeing:
            case State.Finishing:
                GUI.Label(new Rect(20, 20, 900, 50),
                    $"枠内率 {Rate():F1}%　残量 {Mathf.Max(0, remaining):F0}%　{elapsed:F1}秒", hud);
                if (state == State.Peeing && remaining < weakThreshold)
                    GUI.Label(below, "スペース連打で振れ！", mid);
                break;

            case State.Result:
                float r = Rate();
                GUI.Label(center, $"ランク {Rank(r)}", big);
                GUI.Label(below,
                    $"枠内率 {r:F1}%　タイム {elapsed:F1}秒\nあなたの足元に {outCount} 滴\n\nRでもう一度", mid);
                break;
        }
    }
}