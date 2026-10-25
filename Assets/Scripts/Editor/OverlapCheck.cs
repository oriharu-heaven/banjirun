using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 重なった面（Z ファイティング。同じ所に 2 枚の面があり、どちらが手前か決まらずにチラつく）を探す。
/// 向きがほぼ同じ（1.8° 以内）で、すき間が 2mm 以内の三角形どうしが、2cm 角より広く重なっていたら「重なり」とする。
/// 別の物どうしだけでなく、1 つのメッシュの中（部品をまとめたメッシュで箱どうしが重なっている所など）も探す。
/// 裏表で向き合った面は、どちらかが両面描き（Cull Off）の素材のときだけ数える（片面なら裏は描かれないのでチラつかない）。
/// メニュー「BanjiRun/Check Overlapping Surfaces」: VR 版・画面版のシーンの、バンジー（BungeeBuilder が作る物）の重なりを調べる。
/// コマンドから: Unity -batchmode -projectPath &lt;project&gt; -executeMethod OverlapCheck.RunBatch（重なりがあれば終了コード 1）
/// LG の作品にも同じ物がある（LG-Template の LGOverlapCheck。2026-10-19 に banjirun の階段のチラつきから作った）。
/// </summary>
public static class OverlapCheck
{
    const float PlaneTolerance = 0.002f;   // 面と面のすき間がこれ以下なら同じ面とみなす（m）
    const float NormalTolerance = 0.9995f; // 向きの差（cos）。約 1.8°
    const float MinArea = 0.0004f;         // これより狭い重なりは数えない（2cm × 2cm）
    const string BungeeRoot = "BungeeJump";
    const string TimeAttackRoot = TimeAttackBuilder.RootName;
    const string ReportPath = "Builds/OverlapCheck.txt";

    public struct Finding
    {
        public Renderer a, b;
        public float area;
        public Vector3 point;
        public List<string> samples; // 重なった三角形の組の例（場所と向き）。同じメッシュの中のときに、どの部品か探す手がかり
    }

    [MenuItem("BanjiRun/Check Overlapping Surfaces")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Run());
    }

    public static void RunBatch()
    {
        string result = Run();
        Debug.Log(result);
        EditorApplication.Exit(result.StartsWith("OK") ? 0 : 1);
    }

    /// <summary>VR 版・画面版のシーンを開いて、バンジーの物が関わる重なりを数える。結果は 1 行目が OK / NG。</summary>
    static string Run()
    {
        var report = new StringBuilder();
        int total = 0;
        foreach (string path in GameSceneBuilder.OptimizedScenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()).Cast<Renderer>().ToList();
            var findings = Find(renderers, r => Under(r, BungeeRoot) || Under(r, TimeAttackRoot));
            total += findings.Count;
            report.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)}: バンジー・タイムアタックの物が関わる重なり {findings.Count} 組");
            foreach (var f in findings.OrderByDescending(f => f.area).Take(40))
            {
                report.AppendLine("  " + Describe(f));
                foreach (var sample in f.samples) report.AppendLine("      例: " + sample);
            }
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath));
        string head = total == 0 ? "OK: 重なった面はない" : $"NG: 重なった面が {total} 組（{ReportPath}）";
        File.WriteAllText(ReportPath, head + "\n" + report);
        return head + "\n" + report;
    }

    static bool Under(Renderer r, string rootName) => r != null && r.transform.root.name == rootName;

    public static string Describe(Finding f) =>
        $"{PathOf(f.a)} × {(f.a == f.b ? "（同じメッシュの中）" : PathOf(f.b))} / 重なり {f.area * 10000f:F0} cm² / 場所 {f.point:F2}";

    static string PathOf(Renderer r)
    {
        var names = new List<string>();
        for (var t = r.transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }

    // ---- 探す ----

    struct Tri
    {
        public int renderer;
        public Vector3 a, b, c, n;
        public float d;
        public Vector2 min, max; // 面の上の 2D の外形（同じ向きの組でだけ使う）
    }

    /// <summary>
    /// renderers の中で、重なった面の組を探す（同じ組は 1 つにまとめ、重なりの広さを足す）。
    /// focus を渡すと、どちらかが focus に当てはまる組だけを調べる（元からある部屋どうしの組は見ない、など）。
    /// </summary>
    public static List<Finding> Find(IList<Renderer> renderers, System.Func<Renderer, bool> focus = null)
    {
        var focused = new bool[renderers.Count];
        for (int i = 0; i < renderers.Count; i++) focused[i] = focus == null || (renderers[i] != null && focus(renderers[i]));
        var tris = new List<Tri>();
        var doubleSided = new bool[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!r.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue; // TextMesh などメッシュの無い物
            var mesh = filter.sharedMesh;
            doubleSided[i] = r.sharedMaterials.Any(m => m != null && m.HasProperty("_Cull") && m.GetFloat("_Cull") == 0f);
            var m = r.transform.localToWorldMatrix;
            var v = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var idx = mesh.GetTriangles(sub);
                for (int k = 0; k + 2 < idx.Length; k += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[idx[k]]), b = m.MultiplyPoint3x4(v[idx[k + 1]]), c = m.MultiplyPoint3x4(v[idx[k + 2]]);
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.sqrMagnitude < 1e-12f) continue;
                    Vector3 n = cross.normalized;
                    tris.Add(new Tri { renderer = i, a = a, b = b, c = c, n = n, d = Vector3.Dot(n, a) });
                }
            }
        }

        // 向き（裏表は同じ鍵）と、原点からの距離でまとめて、近い物どうしだけ比べる
        var buckets = new Dictionary<(int, int, int, int), List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            Vector3 n = Canonical(t.n, out float sign);
            var key = (Mathf.RoundToInt(n.x * 200f), Mathf.RoundToInt(n.y * 200f), Mathf.RoundToInt(n.z * 200f), Mathf.FloorToInt(t.d * sign / (PlaneTolerance * 2f)));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }

        var pairs = new Dictionary<(int, int), Finding>();
        foreach (var kv in buckets)
        {
            // となりの距離の箱とも比べる（境目をまたいだ組を落とさない）。どちらの箱にも調べる物が無ければ飛ばす
            int own = kv.Value.Count;
            var near = new List<int>(kv.Value);
            var next = (kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Key.Item4 + 1);
            if (buckets.TryGetValue(next, out var more)) near.AddRange(more);
            if (!near.Any(i => focused[tris[i].renderer])) continue;
            Vector3 axisN = Canonical(tris[kv.Value[0]].n, out _);
            Vector3 u = Vector3.Cross(axisN, Mathf.Abs(axisN.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axisN, u);
            var flat = new Vector2[near.Count][];
            var minX = new float[near.Count];
            var maxX = new float[near.Count];
            for (int i = 0; i < near.Count; i++)
            {
                var t = tris[near[i]];
                flat[i] = new[] { new Vector2(Vector3.Dot(t.a, u), Vector3.Dot(t.a, w)), new Vector2(Vector3.Dot(t.b, u), Vector3.Dot(t.b, w)), new Vector2(Vector3.Dot(t.c, u), Vector3.Dot(t.c, w)) };
                minX[i] = Mathf.Min(flat[i][0].x, flat[i][1].x, flat[i][2].x);
                maxX[i] = Mathf.Max(flat[i][0].x, flat[i][1].x, flat[i][2].x);
            }
            // 面の上で横に並べ、横に重なる物どうしだけ比べる（総当たりにしない）
            var order = Enumerable.Range(0, near.Count).OrderBy(i => minX[i]).ToArray();
            for (int oi = 0; oi < order.Length; oi++)
            for (int oj = oi + 1; oj < order.Length && minX[order[oj]] < maxX[order[oi]]; oj++)
            {
                int i = order[oi], j = order[oj];
                if (i >= own && j >= own) continue; // どちらも次の箱の物なら、そちらの番で比べる
                var p = tris[near[i]];
                var q = tris[near[j]];
                if (!focused[p.renderer] && !focused[q.renderer]) continue;
                float dot = Vector3.Dot(p.n, q.n);
                bool sameFacing = dot > NormalTolerance;
                bool facing = dot < -NormalTolerance && (doubleSided[p.renderer] || doubleSided[q.renderer]);
                if (!sameFacing && !facing) continue;
                float gap = sameFacing ? Mathf.Abs(p.d - q.d) : Mathf.Abs(p.d + q.d);
                if (gap > PlaneTolerance) continue;
                if (!BoundsOverlap(flat[i], flat[j])) continue;
                float area = OverlapArea(flat[i], flat[j]);
                if (area <= 1e-6f) continue;
                var key = p.renderer < q.renderer ? (p.renderer, q.renderer) : (q.renderer, p.renderer);
                pairs.TryGetValue(key, out var f);
                if (f.area == 0f) f = new Finding { a = renderers[key.Item1], b = renderers[key.Item2], point = (p.a + p.b + p.c) / 3f, samples = new List<string>() };
                f.area += area;
                if (f.samples.Count < 4) f.samples.Add($"{(p.a + p.b + p.c) / 3f:F3} と {(q.a + q.b + q.c) / 3f:F3}（向き {p.n:F2}、すき間 {gap * 1000f:F1}mm、{area * 10000f:F1} cm²）");
                pairs[key] = f;
            }
        }
        return pairs.Values.Where(f => f.area >= MinArea).ToList();
    }

    /// <summary>裏表で向きが逆の面を同じ鍵にまとめるため、向きをそろえる（sign は掛けた符号）。</summary>
    static Vector3 Canonical(Vector3 n, out float sign)
    {
        float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
        float main = ax >= ay && ax >= az ? n.x : ay >= az ? n.y : n.z;
        sign = main < 0f ? -1f : 1f;
        return n * sign;
    }

    static bool BoundsOverlap(Vector2[] a, Vector2[] b)
    {
        float minAx = Mathf.Min(a[0].x, a[1].x, a[2].x), maxAx = Mathf.Max(a[0].x, a[1].x, a[2].x);
        float minAy = Mathf.Min(a[0].y, a[1].y, a[2].y), maxAy = Mathf.Max(a[0].y, a[1].y, a[2].y);
        float minBx = Mathf.Min(b[0].x, b[1].x, b[2].x), maxBx = Mathf.Max(b[0].x, b[1].x, b[2].x);
        float minBy = Mathf.Min(b[0].y, b[1].y, b[2].y), maxBy = Mathf.Max(b[0].y, b[1].y, b[2].y);
        return minAx < maxBx && minBx < maxAx && minAy < maxBy && minBy < maxAy;
    }

    /// <summary>2 つの三角形（面の上の 2D）の重なりの広さ。三角形 a を三角形 b の 3 辺で順に切っていく。</summary>
    static float OverlapArea(Vector2[] a, Vector2[] b)
    {
        var poly = new List<Vector2>(a);
        if (SignedArea(poly) < 0f) poly.Reverse();
        var clip = new List<Vector2>(b);
        if (SignedArea(clip) < 0f) clip.Reverse();
        for (int e = 0; e < 3 && poly.Count > 0; e++)
        {
            Vector2 c0 = clip[e], c1 = clip[(e + 1) % 3];
            var output = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p0 = poly[i], p1 = poly[(i + 1) % poly.Count];
                bool in0 = Side(c0, c1, p0) >= 0f, in1 = Side(c0, c1, p1) >= 0f;
                if (in0) output.Add(p0);
                if (in0 != in1) output.Add(Intersect(c0, c1, p0, p1));
            }
            poly = output;
        }
        return poly.Count < 3 ? 0f : Mathf.Abs(SignedArea(poly));
    }

    static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    static Vector2 Intersect(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        float s0 = Side(a, b, p), s1 = Side(a, b, q);
        return p + (q - p) * (s0 / (s0 - s1));
    }

    static float SignedArea(List<Vector2> poly)
    {
        float s = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            s += p.x * q.y - q.x * p.y;
        }
        return s / 2f;
    }
}
