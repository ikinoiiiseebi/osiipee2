using UnityEngine;
using UnityEngine.InputSystem;

// 音の管理（BGM・おしっぴーのループ音・効果音・M キーでミュート）
// シーンを読み直しても（R / T）同じものが残り、同じ曲ならそのまま流し続ける
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("BGM")]
    public AudioClip bgmTitleResult;   // タイトルとリザルト
    public AudioClip bgmPlay;          // プレイ中（カウントダウンから）
    [Range(0f, 1f)] public float bgmVolume = 0.5f;
    public float bgmFade = 0.6f;       // 切り替えのフェード（秒）

    [Header("おしっぴーの音（発射中だけループ）")]
    public AudioClip peeLoop;
    [Range(0f, 1f)] public float peeVolume = 0.6f;
    public float pushVolumeScale = 1.3f;   // 踏ん張り中の音量の倍率
    public float pushPitch = 1.15f;        // 踏ん張り中のピッチ

    [Header("効果音")]
    public AudioClip success;          // ジョッキ成功
    public AudioClip stamp;            // リザルトの判子
    public AudioClip fail;             // 失敗（今は空でよい）
    [Range(0f, 1f)] public float seVolume = 0.8f;

    static bool muted;

    AudioSource bgmA, bgmB, pee, se;
    AudioSource bgmCurrent;
    float fadeT = 1f;
    float peeTarget;       // 0（止める）〜1
    bool peePush;

    void Awake()
    {
        // シーンを読み直したときは、前から残っているほうを使い続ける
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        bgmA = NewSource(true);
        bgmB = NewSource(true);
        pee = NewSource(true);
        pee.volume = 0f;
        se = NewSource(false);
        AudioListener.volume = muted ? 0f : 1f;
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;   // 2D
        return s;
    }

    // ---- 呼び出し用（Instance がなくても何も起きない） ----
    public static void TitleResultBgm() { if (Instance != null) Instance.PlayBgm(Instance.bgmTitleResult); }
    public static void PlayBgm() { if (Instance != null) Instance.PlayBgm(Instance.bgmPlay); }
    public static void Success() { if (Instance != null) Instance.PlaySe(Instance.success); }
    public static void Stamp() { if (Instance != null) Instance.PlaySe(Instance.stamp); }
    public static void Fail() { if (Instance != null) Instance.PlaySe(Instance.fail); }

    // 発射中かどうかと、踏ん張り中かどうかを毎フレーム伝える
    public static void Pee(bool on, bool push)
    {
        if (Instance == null) return;
        Instance.peeTarget = on ? 1f : 0f;
        Instance.peePush = push;
    }

    // WebGL ではクリックするまで音が出ないので、最初のクリックで鳴らし直す
    public static void Unlock()
    {
        if (Instance == null) return;
        var c = Instance.bgmCurrent;
        if (c != null && c.clip != null && !c.isPlaying) c.Play();
    }

    void PlayBgm(AudioClip clip)
    {
        if (clip == null) return;
        // 同じ曲が流れていれば、途中から鳴らし直さずそのまま
        if (bgmCurrent != null && bgmCurrent.clip == clip && bgmCurrent.isPlaying) return;

        var next = bgmCurrent == bgmA ? bgmB : bgmA;
        next.clip = clip;
        next.volume = 0f;
        next.Play();
        bgmCurrent = next;
        fadeT = 0f;
    }

    void PlaySe(AudioClip clip)
    {
        if (clip != null) se.PlayOneShot(clip, seVolume);
    }

    void Update()
    {
        // M キーでミュートの切り替え
        var kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame)
        {
            muted = !muted;
            AudioListener.volume = muted ? 0f : 1f;
        }

        // BGM のクロスフェード
        fadeT = bgmFade > 0f ? Mathf.Min(1f, fadeT + Time.unscaledDeltaTime / bgmFade) : 1f;
        foreach (var s in new[] { bgmA, bgmB })
        {
            if (s == bgmCurrent) s.volume = bgmVolume * fadeT;
            else if (s.isPlaying)
            {
                s.volume = Mathf.Min(s.volume, bgmVolume * (1f - fadeT));
                if (fadeT >= 1f) s.Stop();
            }
        }

        // おしっぴーの音（止めるときも一瞬で小さくして、ぷつっと鳴らないようにする）
        if (peeLoop != null)
        {
            if (pee.clip != peeLoop) pee.clip = peeLoop;
            float target = peeTarget * peeVolume * (peePush ? pushVolumeScale : 1f);
            pee.volume = Mathf.MoveTowards(pee.volume, Mathf.Min(1f, target), Time.unscaledDeltaTime * 8f);
            pee.pitch = Mathf.MoveTowards(pee.pitch, peePush ? pushPitch : 1f, Time.unscaledDeltaTime * 2f);
            if (peeTarget > 0f && !pee.isPlaying) pee.Play();
            else if (peeTarget <= 0f && pee.isPlaying && pee.volume <= 0.001f) pee.Stop();
        }
    }

    void OnGUI()
    {
        if (!muted) return;
        GuiScale.Apply();
        var c = Wa.Washi; c.a = 0.9f;
        Ui.Text(new Rect(GuiScale.Width - 260, GuiScale.Height - 44, 240, 36), "消音中（M で解除）", 20, c, TextAnchor.MiddleRight);
    }
}
