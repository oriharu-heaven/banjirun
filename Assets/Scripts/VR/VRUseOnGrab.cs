using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// VR で、手で使える物（IUsable: ドア・窓・ハーネス）をコントローラーでつかんだら使う。
/// 使える物と同じ GameObject に付け（VRSceneBuilder が付ける）、触れるかレイを当ててグリップを押すと Use を呼ぶ。
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class VRUseOnGrab : MonoBehaviour
{
    IUsable usable;
    XRSimpleInteractable interactable;

    void Awake()
    {
        usable = GetComponent<IUsable>();
        interactable = GetComponent<XRSimpleInteractable>();
    }

    void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);
    void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // 人のいない側へ開くよう、頭の位置を渡す（取れなければ手の位置）
        usable?.Use(Camera.main != null ? Camera.main.transform.position : args.interactorObject.transform.position);
    }
}
