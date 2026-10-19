using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BungeeBuilder の部品作り: 鉄骨（トラス）・階段・手すり・電光掲示板・戻る輪の光・Tripo のモデルの置き方・模様（テクスチャ）と素材。
/// 細かい棒や板は 1 つのメッシュにまとめて置く（部品ごとに GameObject を作ると数百個になり、Quest で重い）。
/// 模様（縞・滑り止め・LED の点・電球の光・ロープの編み目）はコードで作って Assets/Bungee/Textures に保存する。
/// </summary>
public static partial class BungeeBuilder
{
    const string TextureFolder = AssetFolder + "/Textures";
    const string ModelFolder = AssetFolder + "/Models";
    const string ChaseBulbsShader = "BanjiRun/ChaseBulbs";
    const string LedTextShader = "BanjiRun/LedText";

    // ---- 部品をまとめるメッシュ ----

    /// <summary>棒・板・四角形をためて、最後に 1 つのメッシュ（素材 1 つ）にする。</summary>
    class Parts
    {
        readonly List<CombineInstance> list = new List<CombineInstance>();
        readonly List<Mesh> temporary = new List<Mesh>();
        static Mesh cube, cylinder;

        static Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        static Quaternion Along(Vector3 dir) =>
            Quaternion.LookRotation(dir, Mathf.Abs(dir.normalized.y) > 0.99f ? Vector3.forward : Vector3.up);

        public bool Empty => list.Count == 0;

        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (cube == null) cube = Primitive(PrimitiveType.Cube);
            list.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(center, rotation, size) });
        }

        /// <summary>角材（断面 width × height）。height は上下方向（できるだけ水平を保つ）。</summary>
        public void Beam(Vector3 from, Vector3 to, float width, float height = -1f)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            Box((from + to) / 2f, new Vector3(width, height < 0f ? width : height, d.magnitude), Along(d));
        }

        /// <summary>丸棒（ワイヤー・ロープの吊り）</summary>
        public void Rod(Vector3 from, Vector3 to, float radius)
        {
            if (cylinder == null) cylinder = Primitive(PrimitiveType.Cylinder);
            Vector3 d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            list.Add(new CombineInstance
            {
                mesh = cylinder,
                transform = Matrix4x4.TRS((from + to) / 2f, Quaternion.FromToRotation(Vector3.up, d), new Vector3(radius * 2f, d.magnitude / 2f, radius * 2f)),
            });
        }

        /// <summary>四角形（裏からは見えない。rotation の +Z を向いて見る人に表が見える）。UV は uv 倍に並べる。</summary>
        public void Quad(Vector3 center, Quaternion rotation, float width, float height, Vector2 uv)
        {
            var mesh = new Mesh();
            float w = width / 2f, h = height / 2f;
            mesh.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f), new Vector3(-w, h, 0f), new Vector3(w, h, 0f) };
            mesh.uv = new[] { Vector2.zero, new Vector2(uv.x, 0f), new Vector2(0f, uv.y), uv };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            temporary.Add(mesh);
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(center, rotation, Vector3.one) });
        }

        /// <summary>まとめて 1 つの GameObject にする（world 座標のまま。原点に置く）。</summary>
        public GameObject Build(Transform parent, string name, string material, bool isStatic = true)
        {
            var mesh = new Mesh { name = name };
            int vertices = list.Sum(c => c.mesh.vertexCount);
            if (vertices > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(list.ToArray(), true, true);
            mesh.RecalculateBounds();
            foreach (var m in temporary) Object.DestroyImmediate(m);
            // 頂点は world 座標なので、world の原点に置いたまま親に入れる
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial(material);
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
            return go;
        }
    }

    /// <summary>四角い鉄骨のトラス（4 本の柱と、面ごとの横材・斜材）。sideHint はトラスの面を決める向き。</summary>
    static void Truss(Parts parts, Vector3 from, Vector3 to, float width, Vector3 sideHint)
    {
        Vector3 axis = to - from;
        float length = axis.magnitude;
        if (length < 1e-4f) return;
        Vector3 dir = axis / length;
        Vector3 side = Vector3.Cross(dir, sideHint);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.forward);
        side.Normalize();
        Vector3 normal = Vector3.Cross(side, dir).normalized;
        float h = width / 2f, chord = width * 0.16f, lace = width * 0.09f;
        Vector3[] corners = { side * h + normal * h, -side * h + normal * h, -side * h - normal * h, side * h - normal * h };
        foreach (var c in corners) parts.Beam(from + c - dir * chord / 2f, to + c + dir * chord / 2f, chord);
        int segments = Mathf.Max(1, Mathf.RoundToInt(length / width));
        for (int i = 0; i <= segments; i++)
        {
            Vector3 p0 = from + axis * (i / (float)segments);
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = corners[k], b = corners[(k + 1) % 4];
                parts.Beam(p0 + a, p0 + b, lace);
                if (i == segments) continue;
                Vector3 p1 = from + axis * ((i + 1) / (float)segments);
                if ((i + k) % 2 == 0) parts.Beam(p0 + a, p1 + b, lace);
                else parts.Beam(p0 + b, p1 + a, lace);
            }
        }
    }

    /// <summary>当たり判定だけの箱（見た目は別に作る）。</summary>
    static GameObject ColliderBox(Transform parent, string name, Vector3 a, Vector3 b)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = (a + b) / 2f;
        go.AddComponent<BoxCollider>().size = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z));
        GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
        return go;
    }

    // ---- 電光掲示板 ----

    struct LedStyle
    {
        public Color[] palette;
        public bool doubleSided;
        public string marquee;
        public int marqueeWidth;
        public string idle;
        public float width, height;   // 0 なら文から決める
        public Color bulbColor;
        public float blinkTime;       // 文字が変わったときの点滅の長さ（0 なら LedSign の既定）
    }

    static readonly Color Amber = new Color(1f, 0.62f, 0.12f), LedRed = new Color(1f, 0.18f, 0.1f), LedGreen = new Color(0.25f, 1f, 0.35f),
        LedCyan = new Color(0.2f, 0.9f, 1f), LedWhite = new Color(1f, 0.95f, 0.85f), LedPink = new Color(1f, 0.3f, 0.75f), LedYellow = new Color(1f, 0.92f, 0.2f);

    /// <summary>
    /// 電光掲示板。position は文字の真ん中、facing は読む人が向く方向（読む人は板の -facing 側に立つ）。
    /// 黒い枠の中に LED の板、その上に光る文字と LED の点の網、縁に流れる電球。返す TextMesh の text を変えると LedSign が点滅させる。
    /// </summary>
    static TextMesh LedBoard(Transform parent, string name, string text, Vector3 position, Vector3 facing, float em, LedStyle style)
    {
        string sizing = !string.IsNullOrEmpty(style.marquee) ? new string('あ', style.marqueeWidth) : text;
        string[] lines = sizing.Split('\n');
        float width = style.width > 0f ? style.width : lines.Max(l => l.Sum(ch => ch < 128 ? 0.6f : 1f)) * em + em * 1.1f;
        float height = style.height > 0f ? style.height : lines.Length * em * 1.2f + em * 0.9f;
        float pitch = Mathf.Clamp(em / 11f, 0.005f, 0.04f); // LED の点の間隔（1 文字に 11 個ほど）
        const float depth = 0.07f;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
        var root = go.transform;
        var front = NewText(go, text, em, style.palette != null && style.palette.Length > 0 ? style.palette[0] : Amber);
        var texts = new List<TextMesh> { front };
        var bulbs = new List<Renderer>();

        // 黒い箱と、前後の縁
        var frame = new Parts();
        Quaternion rot = root.rotation;
        Vector3 L(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
        frame.Box(L(0f, 0f, depth / 2f + 0.008f), new Vector3(width + 0.06f, height + 0.06f, depth), rot);
        float bezel = Mathf.Clamp(em * 0.35f, 0.025f, 0.08f);
        foreach (float z in style.doubleSided ? new[] { 0.002f, depth + 0.016f } : new[] { 0.002f })
        {
            frame.Box(L(0f, height / 2f + bezel / 2f, z), new Vector3(width + bezel * 2f, bezel, 0.025f), rot);
            frame.Box(L(0f, -height / 2f - bezel / 2f, z), new Vector3(width + bezel * 2f, bezel, 0.025f), rot);
            frame.Box(L(width / 2f + bezel / 2f, 0f, z), new Vector3(bezel, height, 0.025f), rot);
            frame.Box(L(-width / 2f - bezel / 2f, 0f, z), new Vector3(bezel, height, 0.025f), rot);
        }
        frame.Build(root, name + "_Frame", "SignFrame");

        // LED の板（消えている点）と、文字の上にかぶせる点の網
        var panel = new Parts();
        var grid = new Parts();
        Vector2 cells = new Vector2(width / pitch, height / pitch);
        panel.Quad(L(0f, 0f, 0.008f), rot, width, height, cells);
        grid.Quad(L(0f, 0f, -0.004f), rot, width, height, cells);
        if (style.doubleSided)
        {
            Quaternion back = rot * Quaternion.Euler(0f, 180f, 0f);
            panel.Quad(L(0f, 0f, depth + 0.012f), back, width, height, cells);
            grid.Quad(L(0f, 0f, depth + 0.024f), back, width, height, cells);
            var backText = NewText(new GameObject(name + "_Back"), text, em, front.color);
            backText.transform.SetParent(root, false);
            backText.transform.localPosition = new Vector3(0f, 0f, depth + 0.018f);
            backText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            texts.Add(backText);
        }
        panel.Build(root, name + "_Panel", "LEDPanel", false);
        grid.Build(root, name + "_Grid", "LEDGrid", false);

        // 縁の電球（番号を頂点色に入れて、シェーダーで流す）
        foreach (bool backSide in style.doubleSided ? new[] { false, true } : new[] { false })
        {
            float spacing = Mathf.Clamp(em * 0.55f, 0.035f, 0.12f), size = spacing * 0.8f;
            float w = width + bezel, hh = height + bezel;
            int nx = Mathf.Max(2, Mathf.RoundToInt(w / spacing)), ny = Mathf.Max(2, Mathf.RoundToInt(hh / spacing));
            var points = new List<Vector2>();
            for (int i = 0; i < nx; i++) points.Add(new Vector2(-w / 2f + w * i / nx, hh / 2f));
            for (int i = 0; i < ny; i++) points.Add(new Vector2(w / 2f, hh / 2f - hh * i / ny));
            for (int i = 0; i < nx; i++) points.Add(new Vector2(w / 2f - w * i / nx, -hh / 2f));
            for (int i = 0; i < ny; i++) points.Add(new Vector2(-w / 2f, -hh / 2f + hh * i / ny));
            float z = backSide ? depth + 0.032f : -0.014f;
            var bulb = BulbStrip(root, $"{name}_Bulbs{(backSide ? "_Back" : "")}", points, z, size, backSide);
            bulb.sharedMaterial = BulbMaterial(style.bulbColor.a > 0f ? style.bulbColor : new Color(1f, 0.85f, 0.4f));
            bulbs.Add(bulb);
        }

        var sign = go.AddComponent<LedSign>();
        sign.texts = texts.ToArray();
        sign.bulbs = bulbs.ToArray();
        sign.palette = style.palette;
        sign.marquee = style.marquee;
        sign.width = style.marqueeWidth > 0 ? style.marqueeWidth : 12;
        sign.idleText = style.idle ?? "";
        sign.em = em;
        sign.maxWidth = width - em * 0.4f;
        if (style.blinkTime > 0f) sign.blinkTime = style.blinkTime;
        if (!string.IsNullOrEmpty(style.marquee)) foreach (var t in texts) t.text = style.marquee.Substring(0, Mathf.Min(sign.width, style.marquee.Length));
        return front;
    }

    static TextMesh NewText(GameObject go, string text, float em, Color color)
    {
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = em * 10f / tm.fontSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.lineSpacing = 0.95f;
        tm.color = color;
        if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = LedTextMaterial();
        return tm;
    }

    /// <summary>電球を並べた 1 つのメッシュ（四角形を並べ、頂点色の r・g に電球の番号を入れる）。</summary>
    static MeshRenderer BulbStrip(Transform root, string name, List<Vector2> points, float z, float size, bool backSide)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        float s = size / 2f;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            int start = vertices.Count;
            float xs = backSide ? -1f : 1f;
            vertices.Add(new Vector3(p.x - s * xs, p.y - s, z));
            vertices.Add(new Vector3(p.x + s * xs, p.y - s, z));
            vertices.Add(new Vector3(p.x - s * xs, p.y + s, z));
            vertices.Add(new Vector3(p.x + s * xs, p.y + s, z));
            uvs.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
            var c = new Color32((byte)(i % 256), (byte)(i / 256), 0, 255);
            colors.AddRange(new[] { c, c, c, c });
            triangles.AddRange(new[] { start, start + 2, start + 3, start, start + 3, start + 1 });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return renderer;
    }

    /// <summary>電光掲示板の文字の素材（フォントの文字の形を使う。実行中のフォントの作り直しは LedSign が追いかける）。</summary>
    static Material LedTextMaterial()
    {
        if (materials.TryGetValue("LEDText", out var cached)) return cached;
        var mat = new Material(Shader.Find(LedTextShader));
        mat.SetTexture("_MainTex", font.material.mainTexture);
        mat.renderQueue = (int)RenderQueue.Transparent;
        return SaveMaterial("LEDText", mat);
    }

    static Material BulbMaterial(Color on)
    {
        string key = $"Bulbs_{ColorUtility.ToHtmlStringRGB(on)}";
        if (materials.TryGetValue(key, out var cached)) return cached;
        var mat = new Material(Shader.Find(ChaseBulbsShader));
        mat.SetTexture("_BaseMap", BulbTexture());
        mat.SetColor("_OnColor", on);
        mat.SetColor("_OffColor", new Color(on.r * 0.22f, on.g * 0.18f, on.b * 0.14f, 0.9f));
        return SaveMaterial(key, mat);
    }

    // ---- 戻る輪の光 ----

    /// <summary>光る輪（平たい輪。dashes が 0 より大きければ、とぎれとぎれの輪）。</summary>
    static Mesh RingMesh(float inner, float outer, int segments, int dashes)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            if (dashes > 0 && (i * dashes * 2 / segments) % 2 == 1) continue;
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            int start = vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner));
            vertices.Add(new Vector3(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer));
            vertices.Add(new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner));
            vertices.Add(new Vector3(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer));
            uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f) });
            triangles.AddRange(new[] { start, start + 1, start + 3, start, start + 3, start + 2 });
        }
        var mesh = new Mesh { name = "Ring" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>上に向かって消えていく光の筒（ふたなし）。</summary>
    static Mesh TubeMesh(float radius, float height, int segments)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            vertices.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            vertices.Add(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            uvs.Add(new Vector2(i / (float)segments, 0f));
            uvs.Add(new Vector2(i / (float)segments, 1f));
            if (i == segments) continue;
            int s = i * 2;
            triangles.AddRange(new[] { s, s + 1, s + 3, s, s + 3, s + 2 });
        }
        var mesh = new Mesh { name = "Tube" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject MeshObject(Transform parent, string name, Mesh mesh, string material, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetMaterial(material);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    /// <summary>クッションの上の戻る輪: 光る輪・回る内側の輪・上へ消える光の筒・のぼる光の粒。</summary>
    static void ReturnPadEffect(Transform parent, Vector3 center)
    {
        var root = new GameObject("ReturnPad_Effect").transform;
        root.SetParent(parent, false);
        root.position = center;
        MeshObject(root, "Ring", RingMesh(0.42f, 0.52f, 48, 0), "ReturnRing", center + Vector3.up * 0.012f);
        var inner = MeshObject(root, "InnerRing", RingMesh(0.26f, 0.34f, 48, 8), "ReturnRing", center + Vector3.up * 0.014f);
        var spin = inner.AddComponent<SpinPulse>();
        spin.spin = new Vector3(0f, 70f, 0f);
        spin.pulseAmount = 0.06f;
        var beam = MeshObject(root, "Beam", TubeMesh(0.47f, 1.8f, 32), "ReturnBeam", center);
        var beamSpin = beam.AddComponent<SpinPulse>();
        beamSpin.spin = new Vector3(0f, -25f, 0f);
        beamSpin.pulseAmount = 0.12f;
        beamSpin.pulseSpeed = 2.4f;
        beamSpin.pulseHeightOnly = true;

        var particles = new GameObject("Sparks").AddComponent<ParticleSystem>();
        particles.transform.SetParent(root, false);
        particles.transform.SetPositionAndRotation(center + Vector3.up * 0.05f, Quaternion.Euler(-90f, 0f, 0f));
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.startLifetime = 1.6f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new Color(0.5f, 1f, 1f, 1f);
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 14f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.45f;
        shape.radiusThickness = 0.2f;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetMaterial("ReturnSpark");
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        particles.Play();
    }

    // ---- Tripo のモデル ----

    /// <summary>
    /// Assets/Bungee/Models/&lt;name&gt;/&lt;name&gt;.fbx を、外形が size（モデルの向きで 横・高さ・奥）に収まるよう縮めて置く。
    /// 底の真ん中が position、モデルの前（+Z）が forward を向く。stretch なら縦横奥を別々に合わせる（0 の向きは、ほかの向きの平均の倍率）。無ければ null。
    /// </summary>
    static Transform PlaceModel(Transform parent, string name, Vector3 position, Vector3 forward, Vector3 size, bool stretch)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{name}/{name}.fbx");
        if (asset == null) return null;
        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        holder.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        var fit = new GameObject("Fit").transform;
        fit.SetParent(holder, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, fit);
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        Bounds b = LocalBounds(holder, model);
        Vector3 scale;
        if (stretch)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < 3; i++) if (size[i] > 0f) { sum += size[i] / b.size[i]; count++; }
            float mean = count > 0 ? sum / count : 1f;
            scale = new Vector3(size.x > 0f ? size.x / b.size.x : mean, size.y > 0f ? size.y / b.size.y : mean, size.z > 0f ? size.z / b.size.z : mean);
        }
        else
        {
            float s = float.MaxValue;
            for (int i = 0; i < 3; i++) if (size[i] > 0f) s = Mathf.Min(s, size[i] / b.size[i]);
            scale = Vector3.one * s;
        }
        fit.localScale = scale;
        b = LocalBounds(holder, model);
        fit.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        foreach (var t in holder.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticFlags);
        return holder;
    }

    /// <summary>space から見た、go の中のメッシュの外形。</summary>
    static Bounds LocalBounds(Transform space, GameObject go)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var m = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var mb = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    /// <summary>モデルの上面の高さ（真上から下へ当てて測る。測れなければ fallback）。</summary>
    static float SurfaceHeight(Transform model, Vector3 at, float fallback)
    {
        var temporary = new List<MeshCollider>();
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var c = filter.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = filter.sharedMesh;
            temporary.Add(c);
        }
        Physics.SyncTransforms();
        float height = fallback;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 20f, Vector3.down, 40f))
            if (temporary.Contains(hit.collider as MeshCollider)) { height = hit.point.y; break; }
        foreach (var c in temporary) Object.DestroyImmediate(c);
        return height;
    }

    // ---- 模様（コードで作る PNG） ----

    static Texture2D GeneratedTexture(string name, int w, int h, System.Func<int, int, Color> pixel, bool alpha,
        bool normalMap = false, TextureWrapMode wrap = TextureWrapMode.Repeat, bool mipmaps = true)
    {
        string path = $"{TextureFolder}/Bungee_{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, pixel(x, y));
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        Directory.CreateDirectory(TextureFolder);
        bool changed = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png);
        if (changed) File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool dirty = changed;
        void Set<T>(T current, T value, System.Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            apply(value);
            dirty = true;
        }
        Set(importer.textureType, normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default, v => importer.textureType = v);
        Set(importer.alphaIsTransparency, alpha, v => importer.alphaIsTransparency = v);
        Set(importer.wrapMode, wrap, v => importer.wrapMode = v);
        Set(importer.mipmapEnabled, mipmaps, v => importer.mipmapEnabled = v);
        Set(importer.sRGBTexture, !normalMap, v => importer.sRGBTexture = v);
        if (dirty) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Smooth(float edge0, float edge1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

    /// <summary>縞鋼板（滑り止めの凸）の高さ 0〜1。128 画素で 2 × 2 個の凸。</summary>
    static float DiamondHeight(int x, int y)
    {
        float best = 0f;
        for (int k = 0; k < 2; k++)
        {
            // 2 方向（互い違い）の細長い凸
            float cx = ((x + (k == 0 ? 0 : 32)) % 64) - 32f, cy = ((y + (k == 0 ? 0 : 32)) % 64) - 32f;
            float a = k == 0 ? 0.785f : -0.785f;
            float u = cx * Mathf.Cos(a) - cy * Mathf.Sin(a), v = cx * Mathf.Sin(a) + cy * Mathf.Cos(a);
            float d = Mathf.Sqrt(u * u / (13f * 13f) + v * v / (3.2f * 3.2f));
            best = Mathf.Max(best, 1f - Smooth(0.75f, 1f, d));
        }
        return best;
    }

    static Texture2D DiamondPlateAlbedo() => GeneratedTexture("DiamondPlate", 128, 128, (x, y) =>
    {
        float h = DiamondHeight(x, y);
        float noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.08f;
        float g = 0.42f + noise + h * 0.12f;
        return new Color(g, g * 1.01f, g * 1.04f, 1f);
    }, alpha: false);

    static Texture2D DiamondPlateNormal() => GeneratedTexture("DiamondPlate_N", 128, 128, (x, y) =>
    {
        float dx = DiamondHeight((x + 1) % 128, y) - DiamondHeight((x + 127) % 128, y);
        float dy = DiamondHeight(x, (y + 1) % 128) - DiamondHeight(x, (y + 127) % 128);
        var n = new Vector3(-dx * 2.5f, -dy * 2.5f, 1f).normalized;
        return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
    }, alpha: false, normalMap: true);

    static Texture2D HazardTexture() => GeneratedTexture("Hazard", 64, 64, (x, y) =>
        (x + y) % 64 < 32 ? new Color(1f, 0.78f, 0.06f) : new Color(0.06f, 0.06f, 0.07f), alpha: false);

    /// <summary>電球の光（真ん中が明るく、まわりがぼんやり光る）。光の粒にも使う。</summary>
    static Texture2D BulbTexture() => GeneratedTexture("Bulb", 64, 64, (x, y) =>
    {
        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f)) / 32f;
        float core = Mathf.Clamp01((0.55f - d) / 0.12f);
        float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2f) * 0.55f;
        return new Color(1f, 1f, 1f, Mathf.Max(core, halo));
    }, alpha: true, wrap: TextureWrapMode.Clamp);

    /// <summary>LED の板の消えている点（1 マスに 1 つ）。</summary>
    static Texture2D LedPanelTexture() => GeneratedTexture("LEDPanel", 32, 32, (x, y) =>
    {
        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
        float dot = 1f - Smooth(0.62f, 0.78f, d);
        return Color.Lerp(new Color(0.02f, 0.02f, 0.025f), new Color(0.11f, 0.06f, 0.04f), dot);
    }, alpha: false);

    /// <summary>文字の上にかぶせる網（点の所だけ透ける）。</summary>
    static Texture2D LedGridTexture() => GeneratedTexture("LEDGrid", 32, 32, (x, y) =>
    {
        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
        return new Color(0f, 0f, 0f, Smooth(0.6f, 0.8f, d));
    }, alpha: true);

    /// <summary>編んだロープの模様。</summary>
    static Texture2D RopeTexture() => GeneratedTexture("Rope", 64, 16, (x, y) =>
    {
        float braid = Mathf.Repeat((x * 2f + y * 4f) / 16f, 1f);
        float shade = 0.75f + 0.25f * Mathf.Sin(braid * Mathf.PI);
        float edge = 1f - Mathf.Abs(y - 7.5f) / 9f;
        float k = shade * (0.65f + 0.35f * edge);
        return new Color(0.98f * k, 0.36f * k, 0.08f * k, 1f);
    }, alpha: false);

    /// <summary>上に向かって消える光（光の筒）。</summary>
    static Texture2D BeamTexture() => GeneratedTexture("Beam", 8, 64, (x, y) =>
    {
        float t = y / 63f;
        float stripes = 0.75f + 0.25f * Mathf.Sin(t * 40f);
        return new Color(1f, 1f, 1f, Mathf.Pow(1f - t, 1.6f) * stripes);
    }, alpha: true, wrap: TextureWrapMode.Clamp);

    // ---- 素材 ----

    static Material Lit(Color color, float smoothness, float metallic, Texture2D baseMap = null, Texture2D normal = null, Vector2? tiling = null)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (tiling.HasValue) mat.SetTextureScale("_BaseMap", tiling.Value);
        return mat;
    }

    /// <summary>光る（ライトの影響を受けない）素材。mode: "opaque" / "alpha"（半透明）/ "add"（足して光らせる）。</summary>
    static Material Glow(Color color, Texture2D baseMap, Vector2 tiling, string mode, bool doubleSided, int queueOffset = 0)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color);
        if (baseMap != null)
        {
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTextureScale("_BaseMap", tiling);
        }
        if (mode != "opaque")
        {
            bool add = mode == "add";
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", add ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", add ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", add ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_QueueOffset", queueOffset); // URP は描く順をこの値から決め直すので、renderQueue だけでなくここにも入れる
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent + queueOffset;
        }
        if (doubleSided) mat.SetFloat("_Cull", 0f);
        return mat;
    }
}
