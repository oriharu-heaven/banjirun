using UnityEngine;

/// <summary>
/// ジャンプ台のモード切り替えのレバー（BungeeBuilder が手すりに付ける）。使う（VR はグリップ、画面版は E キーなど）と「ふつう ⇔ ドッキリ」が切り替わる。
/// ドッキリ: 板の先でカウントダウンを待っていると、途中で板が折れて落ちる（BungeeJump.SurpriseMode）。
/// 今のモードは、上の電光掲示板と、レバーの向き（上 = ふつう、下 = ドッキリ）で分かる。
/// </summary>
public class BungeeModeSwitch : MonoBehaviour, IUsable
{
    public BungeeJump bungee;
    public Transform lever;           // 付け根が原点。ローカルの x 軸まわりに倒す
    public float normalAngle = -40f;  // ふつう（上に倒す）
    public float surpriseAngle = 40f; // ドッキリ（下に倒す）
    public TextMesh sign;
    public Color[] normalColors = { new Color(0.25f, 1f, 0.35f), new Color(0.2f, 0.9f, 1f) };
    public Color[] surpriseColors = { new Color(1f, 0.18f, 0.1f), new Color(1f, 0.3f, 0.75f) };

    const string NormalText = "ふつうモード\nレバーで切り替え";
    const string SurpriseText = "ドッキリモード\n何かが起こる…";

    float angle;
    LedSign led;

    public string UseHint => bungee != null && bungee.SurpriseMode ? "ふつうモードにする" : "ドッキリモードにする";

    void Start()
    {
        if (sign != null) led = sign.GetComponentInParent<LedSign>();
        angle = TargetAngle;
        ApplyLever();
        Show();
        if (bungee != null) bungee.SurpriseModeChanged += _ => Show();
    }

    public void Use(Vector3 actor)
    {
        if (bungee != null) bungee.SetSurpriseMode(!bungee.SurpriseMode, transform.position);
    }

    float TargetAngle => bungee != null && bungee.SurpriseMode ? surpriseAngle : normalAngle;

    void Update()
    {
        angle = Mathf.MoveTowards(angle, TargetAngle, 500f * Time.deltaTime);
        ApplyLever();
    }

    void ApplyLever()
    {
        if (lever != null) lever.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }

    void Show()
    {
        bool surprise = bungee != null && bungee.SurpriseMode;
        if (sign != null) sign.text = surprise ? SurpriseText : NormalText;
        if (led != null) led.SetPalette(surprise ? surpriseColors : normalColors);
    }
}
