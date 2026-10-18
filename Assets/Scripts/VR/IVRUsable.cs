using UnityEngine;

/// <summary>
/// 手で使える物（ドア・窓・ハーネス）。VR ではコントローラーのグリップ、パソコンでは E キー / クリックで Use が呼ばれる。
/// </summary>
public interface IVRUsable
{
    /// <summary>使うときの案内（パソコン操作の画面に出す）</summary>
    string UseHint { get; }

    /// <param name="actor">使った人の頭の位置</param>
    void Use(Vector3 actor);
}
