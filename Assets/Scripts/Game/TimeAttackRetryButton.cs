using UnityEngine;

/// <summary>
/// タイムアタックを最初からやり直すボタン（スタート地点とゴール地点の横に置く）。
/// 画面版では E キー / クリック、VR 版ではコントローラーのグリップ（VRUseOnGrab）で押すとスタート地点へ戻り、カウントダウンから再開する。
/// </summary>
public class TimeAttackRetryButton : MonoBehaviour, IUsable
{
    public TimeAttackManager manager;
    public Transform cap;

    float pressTimer;
    Vector3 capRest;

    public string UseHint => "エレベーターを止めて扉を開ける（スタート）";

    void Awake()
    {
        if (cap != null) capRest = cap.localPosition;
    }

    public void Use(Vector3 actor)
    {
        pressTimer = 0.25f;
        if (manager != null) manager.OpenElevator();
    }

    void Update()
    {
        if (cap == null || pressTimer <= 0f) return;
        pressTimer = Mathf.Max(0f, pressTimer - Time.deltaTime);
        float sink = Mathf.Sin(pressTimer / 0.25f * Mathf.PI) * 0.018f;
        cap.localPosition = capRest + Vector3.down * sink;
    }
}
