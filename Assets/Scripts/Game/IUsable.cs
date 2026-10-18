using UnityEngine;

/// <summary>
/// 手で使える物（ドア・窓・ハーネス）。VR ではコントローラーのグリップ（VRUseOnGrab）、
/// 画面で遊ぶときは E キー / クリック / ゲームパッドの X（FirstPersonControls）で Use が呼ばれる。
/// </summary>
public interface IUsable
{
    /// <summary>使うときの案内（画面に出す）</summary>
    string UseHint { get; }

    /// <param name="actor">使った人の頭の位置</param>
    void Use(Vector3 actor);
}
