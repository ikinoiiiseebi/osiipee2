using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ParticleSystem))]
public class PeeController : MonoBehaviour
{
    [Header("狙い（度）")]
    public float yawRange = 30f;    // 左右の振れ幅
    public float downAngle = 40f;   // マウス最下部での下向き角度
    public float upAngle = -20f;    // マウス最上部での上向き角度

    [Header("勢い")]
    public float normalSpeed = 2.5f;
    public float pushSpeed = 4f;    // 左クリック（踏ん張り）中

    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
    int inCount, outCount;

    void Awake() { ps = GetComponent<ParticleSystem>(); }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // マウス位置で狙う方向を決める
        Vector2 p = mouse.position.ReadValue();
        float nx = Mathf.Clamp01(p.x / Screen.width);
        float ny = Mathf.Clamp01(p.y / Screen.height);
        float yaw = Mathf.Lerp(-yawRange, yawRange, nx);
        float pitch = Mathf.Lerp(downAngle, upAngle, ny);
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);

        // 踏ん張り
        var main = ps.main;
        main.startSpeed = mouse.leftButton.isPressed ? pushSpeed : normalSpeed;
    }

    void OnParticleCollision(GameObject other)
    {
        int n = ps.GetCollisionEvents(other, events);
        if (other.CompareTag("Urinal")) inCount += n;
        else if (other.CompareTag("Out")) outCount += n;
    }

    void OnGUI()
    {
        int total = inCount + outCount;
        float rate = total == 0 ? 100f : inCount * 100f / total;
        GUI.skin.label.fontSize = 32;
        GUI.Label(new Rect(20, 20, 800, 50),
            $"枠内率 {rate:F1}%  (in {inCount} / out {outCount})");
    }
}