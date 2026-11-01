using UnityEngine;

// おしっこの見た目だけを液体っぽくする（当たり判定・発射数・数え方は変えない）
// ・粒を速さの方向に伸ばし（Stretched Billboard）、前後と重ねて1本の流れに見せる
// ・中心が白っぽく光り、ふちが琥珀色のツヤのあるテクスチャを実行時に作って貼る
// Pee に付けておけば Inspector で調整できる（付けていなくても PeeController が自動で付ける）
[RequireComponent(typeof(ParticleSystem))]
public class PeeLook : MonoBehaviour
{
    [Header("伸ばし方")]
    public float lengthScale = 2.5f;     // 粒の大きさに対する伸び
    public float velocityScale = 0.025f; // 速さに比例した伸び

    [Header("色")]
    public Color core = new Color(1f, 0.98f, 0.8f);   // 中心のツヤ
    public Color edge = new Color(1f, 0.7f, 0.05f);   // ふちの琥珀色

    static Texture2D tex;
    static Material mat;

    void Start()
    {
        var pc = GetComponent<PeeController>();
        Apply(GetComponent<ParticleSystem>(), true);
        if (pc != null && pc.splash != null) Apply(pc.splash, false);
    }

    void Apply(ParticleSystem ps, bool stretch)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (r == null) return;

        if (mat == null)
        {
            mat = new Material(r.sharedMaterial) { name = "PeeLiquid (runtime)" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", MakeTexture());
            mat.mainTexture = MakeTexture();
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        }
        r.sharedMaterial = mat;

        if (stretch)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = lengthScale;
            r.velocityScale = velocityScale;
            r.cameraVelocityScale = 0f;
        }

        // 色はテクスチャで付けるので、粒の色は白にする（見た目だけの変更）
        var main = ps.main;
        main.startColor = Color.white;
    }

    Texture2D MakeTexture()
    {
        if (tex != null) return tex;
        const int S = 64;
        tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Clamp, name = "PeeLiquid" };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                // ふちに向かって琥珀色、中心は明るい
                Color c = Color.Lerp(core, edge, Mathf.SmoothStep(0f, 1f, d / 0.8f));
                // 左上に小さなハイライト（ツヤ）
                float hd = Vector2.Distance(new Vector2(u, v), new Vector2(-0.25f, 0.3f));
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(1f - hd / 0.3f) * 0.8f);
                c.a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, d)) * 0.95f;
                px[y * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }
}
