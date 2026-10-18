using UnityEngine;

/// <summary>
/// ジャンプ台のフックに掛かっているハーネス。使う（VR はグリップ、画面版は E キーなど）と体に付き、飛ぶ準備ができる。
/// </summary>
public class BungeeHarness : MonoBehaviour, IUsable
{
    public BungeeJump bungee;
    public GameObject model;      // フックに掛かっている見た目
    public Transform cordPoint;   // ロープが付いている所

    Collider[] colliders;

    public string UseHint => "ハーネスを付ける";

    void Awake() => colliders = GetComponentsInChildren<Collider>(true);

    public void Use(Vector3 actor) => bungee.AttachHarness();

    /// <summary>フックに掛ける（true）か、体に付けて見えなくする（false）。</summary>
    public void SetOnHook(bool onHook)
    {
        if (model != null) model.SetActive(onHook);
        foreach (var c in colliders) c.enabled = onHook;
    }
}
