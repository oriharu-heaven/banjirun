using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// VR で、バンジーの怖い瞬間にコントローラーを震わせる（XR Origin に付ける。VRSceneBuilder が付ける）。
/// 画面版は画面を揺らす（FlatCameraEffects）が、VR で視界を勝手に動かすと酔うので、代わりに手に伝える。
/// ・ドッキリで板がきしんだ（予兆）・折れた瞬間
/// ・ロープがピンと張った瞬間
/// ・クッションに着地したとき
/// コントローラーの HapticImpulsePlayer（XRI のサンプルの XR Origin に付いている）を使う。
/// </summary>
public class VRBungeeHaptics : MonoBehaviour
{
    readonly List<HapticImpulsePlayer> players = new List<HapticImpulsePlayer>();

    void Start()
    {
        GetComponentsInChildren(true, players);
        var bungee = FindFirstObjectByType<BungeeJump>();
        if (bungee == null) return;
        bungee.PlankCracked += strength => Pulse(strength >= 1f ? 1f : 0.55f, strength >= 1f ? 0.7f : 0.3f);
        bungee.CordSnapped += strength => Pulse(0.35f + 0.65f * strength, 0.35f);
        bungee.PhaseChanged += phase =>
        {
            if (phase == BungeeJump.Phase.Landed) Pulse(0.6f, 0.2f);
        };
    }

    void Pulse(float amplitude, float duration)
    {
        foreach (var player in players)
            if (player != null && player.isActiveAndEnabled) player.SendHapticImpulse(amplitude, duration);
    }
}
