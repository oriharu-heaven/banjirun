using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// VR でドア・窓を開け閉めする。ドアのピボット（Door と同じ GameObject）に付け、
/// コントローラーで触れるかレイを当ててグリップを押すと Door.Toggle を呼ぶ（パソコンでは E キー）。
/// </summary>
[RequireComponent(typeof(Door), typeof(XRSimpleInteractable))]
public class VRDoorHandle : MonoBehaviour, IVRUsable
{
    Door door;
    XRSimpleInteractable interactable;

    public string UseHint => door.IsOpen ? "閉める" : "開ける";

    void Awake()
    {
        door = GetComponent<Door>();
        interactable = GetComponent<XRSimpleInteractable>();
    }

    void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);
    void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // 人のいない側へ開くよう、頭の位置を渡す（取れなければ手の位置）
        Use(Camera.main != null ? Camera.main.transform.position : args.interactorObject.transform.position);
    }

    public void Use(Vector3 actor) => door.Toggle(actor);
}
