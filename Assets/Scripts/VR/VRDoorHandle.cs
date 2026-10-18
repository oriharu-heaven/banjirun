using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// VR でドアを開け閉めする。ドアのピボット（Door と同じ GameObject）に付け、
/// コントローラーで触れるかレイを当ててグリップを押すと Door.Toggle を呼ぶ。
/// </summary>
[RequireComponent(typeof(Door), typeof(XRSimpleInteractable))]
public class VRDoorHandle : MonoBehaviour
{
    Door door;
    XRSimpleInteractable interactable;

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
        Vector3 actor = Camera.main != null ? Camera.main.transform.position : args.interactorObject.transform.position;
        door.Toggle(actor);
    }
}
