using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// ジャンプ台のフックに掛かっているハーネス。つかむ（グリップ / E キー）と体に付き、飛ぶ準備ができる。
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class BungeeHarness : MonoBehaviour, IVRUsable
{
    public BungeeJump bungee;
    public GameObject model;      // フックに掛かっている見た目
    public Transform cordPoint;   // ロープが付いている所

    XRSimpleInteractable interactable;
    Collider[] colliders;

    public string UseHint => "ハーネスを付ける";

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        colliders = GetComponentsInChildren<Collider>(true);
    }

    void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);
    void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

    void OnSelectEntered(SelectEnterEventArgs args) => Use(args.interactorObject.transform.position);

    public void Use(Vector3 actor) => bungee.AttachHarness();

    /// <summary>フックに掛ける（true）か、体に付けて見えなくする（false）。</summary>
    public void SetOnHook(bool onHook)
    {
        if (model != null) model.SetActive(onHook);
        foreach (var c in colliders) c.enabled = onHook;
    }
}
