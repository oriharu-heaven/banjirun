using UnityEngine;

/// <summary>
/// 電光掲示板。BungeeBuilder が作る案内板に付く。
/// ・縁の電球が流れるように点滅する（電球は 1 つのメッシュ。流れはシェーダー BanjiRun/ChaseBulbs が計算し、ここは点滅のときに全部点ける・消すだけ）
/// ・文字が変わると数回点滅する（BungeeJump は TextMesh.text を書き換えるだけでよい）
/// ・文字の色を順に変える（palette）
/// ・marquee に文を入れると、電光ニュースのように 1 文字ずつ横に流す（width 文字分を見せる）
/// ・文字が空のときは idleText を薄く出す（何も映っていない黒い板にしない）
/// 裏表の両方に文字があるときは texts に 2 つ入れる（同じ文を出す）。
/// </summary>
public class LedSign : MonoBehaviour
{
    public TextMesh[] texts;
    public Renderer[] bulbs;            // 縁の電球（表と裏）
    public Color[] palette;             // 文字の色（空なら最初の色のまま）
    public float colorInterval = 1.2f;  // 色を変える間隔（秒）
    public float blinkTime = 0.9f;      // 文字が変わったときの点滅の長さ
    [TextArea] public string marquee;   // 流す文（空なら流さない）
    public int width = 12;              // 流すときに見せる文字数
    public float marqueeStep = 0.14f;   // 1 文字ずらす間隔（秒）
    public string idleText = "";
    public float em = 0.1f;             // 1 文字の大きさ（m）
    public float maxWidth;              // 0 より大きければ、文がこの幅（m）に収まるよう文字を小さくする
    public Color idleColor = new Color(0.35f, 0.18f, 0.05f);

    string shown;
    float blink;
    float colorTimer;
    int colorIndex;
    float marqueeTimer;
    int marqueeOffset;
    Color baseColor;
    MaterialPropertyBlock block;
    float[] baseSize;
    float bulbMode = -1f;
    static readonly int ModeId = Shader.PropertyToID("_Mode");

    void Awake()
    {
        if (texts == null || texts.Length == 0) return;
        // 文字の素材はフォントの文字の形（動的フォントは新しい文字が要ると作り直される）を使うので、自分用の複製にして追いかける
        foreach (var t in texts)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r != null && t.font != null && r.sharedMaterial != t.font.material) r.material.mainTexture = t.font.material.mainTexture;
        }
        Font.textureRebuilt += OnFontRebuilt;
        baseSize = new float[texts.Length];
        for (int i = 0; i < texts.Length; i++) baseSize[i] = texts[i].characterSize;
        baseColor = palette != null && palette.Length > 0 ? palette[0] : texts[0].color;
        shown = texts[0].text;
        if (string.IsNullOrEmpty(marquee)) SetAll(Display);
    }

    void OnDestroy() => Font.textureRebuilt -= OnFontRebuilt;

    void OnFontRebuilt(Font rebuilt)
    {
        foreach (var t in texts)
        {
            if (t == null || t.font != rebuilt) continue;
            var r = t.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial != rebuilt.material) r.sharedMaterial.mainTexture = rebuilt.material.mainTexture;
        }
    }

    /// <summary>今見せる文（空なら idleText）</summary>
    string Display => string.IsNullOrEmpty(shown) ? idleText : shown;

    void Update()
    {
        if (texts == null || texts.Length == 0) return;
        float dt = Time.deltaTime;

        // BungeeJump などが文字を書き換えたら、点滅させる（裏の文字もそろえる）
        if (string.IsNullOrEmpty(marquee))
        {
            string current = texts[0].text;
            string written = !string.IsNullOrEmpty(idleText) && current == idleText ? "" : current;
            if (written != shown)
            {
                shown = written;
                if (!string.IsNullOrEmpty(shown)) blink = blinkTime;
                SetAll(Display);
            }
            else if (current != Display) SetAll(Display);
        }

        if (!string.IsNullOrEmpty(marquee))
        {
            marqueeTimer += dt;
            if (marqueeTimer >= marqueeStep)
            {
                marqueeTimer = 0f;
                marqueeOffset = (marqueeOffset + 1) % marquee.Length;
                string loop = marquee + marquee;
                SetAll(loop.Substring(marqueeOffset, Mathf.Min(width, marquee.Length)));
            }
        }

        if (palette != null && palette.Length > 1)
        {
            colorTimer += dt;
            if (colorTimer >= colorInterval)
            {
                colorTimer = 0f;
                colorIndex = (colorIndex + 1) % palette.Length;
                baseColor = palette[colorIndex];
            }
        }

        Color color = baseColor;
        if (string.IsNullOrEmpty(shown) && string.IsNullOrEmpty(marquee)) color = idleColor;
        if (blink > 0f)
        {
            blink -= dt;
            if (Mathf.Repeat(blink, 0.3f) < 0.13f) color *= 0.15f;
        }
        foreach (var t in texts) t.color = color;

        // 縁の電球: ふだんは流れる。文字が点滅している間は、文字と一緒に全部点いたり消えたりする
        float mode = blink > 0f ? (Mathf.Repeat(blink, 0.3f) < 0.13f ? 2f : 1f) : 0f;
        if (bulbs != null && mode != bulbMode)
        {
            bulbMode = mode;
            block ??= new MaterialPropertyBlock();
            foreach (var bulb in bulbs)
            {
                if (bulb == null) continue;
                bulb.GetPropertyBlock(block);
                block.SetFloat(ModeId, mode);
                bulb.SetPropertyBlock(block);
            }
        }
    }

    void SetAll(string text)
    {
        float fit = 1f;
        if (maxWidth > 0f && !string.IsNullOrEmpty(text))
        {
            float widest = 0f;
            foreach (var line in text.Split('\n'))
            {
                float w = 0f;
                foreach (char ch in line) w += ch < 128 ? 0.6f : 1f;
                widest = Mathf.Max(widest, w * em);
            }
            if (widest > maxWidth) fit = maxWidth / widest;
        }
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].text = text;
            if (baseSize != null) texts[i].characterSize = baseSize[i] * fit;
        }
    }
}
