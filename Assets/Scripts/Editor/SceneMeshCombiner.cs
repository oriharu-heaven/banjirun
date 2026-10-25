using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// VR・画面版のシーンをビルドするとき（とエディターで再生するとき）、部屋の動かない物を「素材 × ライトマップ × 場所（4m の升目）」ごとに
/// 1 つのメッシュにまとめ、描画の回数を減らす。静的バッチ（Unity の機能）は SRP Batcher と一緒だと 1 物 1 回の描画のままなので、代わりにここで数を減らす。
/// まとめたメッシュはビルドの中にだけ入り、シーンのファイルは変えない（保存すると git が数十 MB 増えるため）。
/// コライダーは元の物に残す。ドア・透明な物・裏返しの物はまとめない。
/// </summary>
public class SceneMeshCombiner : IProcessSceneWithReport
{
    // 升目の大きさ（m）。大きくすると描画の回数は減るが、見えない所もまとめて描くので三角形が増える
    const float CellSize = 4f;
    // ライトプローブで照らす小物は、まとめた物の真ん中で明るさを 1 つ取るので、高さも分ける
    const float ProbeLitCellHeight = 1.5f;
    // 窓の外の街は遠くから見るだけなので大きくまとめる
    const string ExteriorGroup = "Grp_Exterior";
    const float ExteriorCellSize = 40f;

    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!GameSceneBuilder.OptimizedScenes.Contains(scene.path)) return;
        var room = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<RoomLightDimmer>() != null);
        if (room != null) Combine(room);
    }

    struct Part
    {
        public MeshRenderer renderer;
        public Mesh mesh;
        public int subMesh;
    }

    /// <summary>部屋の物をまとめる。元の物の MeshRenderer と MeshFilter は消える。まとめたメッシュの数を返す。</summary>
    public static int Combine(GameObject room)
    {
        var moving = new HashSet<Transform>(room.GetComponentsInChildren<Door>(true).SelectMany(d => d.GetComponentsInChildren<Transform>(true)));
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("EV_CarDoors") || t.name.StartsWith("EV_LandingDoors") || t.name.StartsWith("CasterWagon"))
            {
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                    moving.Add(child);
            }
        }
        var exterior = room.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == ExteriorGroup);

        var groups = new Dictionary<(Material material, int lightmap, Vector3Int cell, ShadowCastingMode shadows), List<Part>>();
        var sources = new List<MeshRenderer>();
        var skipped = new Dictionary<string, int>();
        void Skip(string reason) => skipped[reason] = skipped.TryGetValue(reason, out int n) ? n + 1 : 1;
        foreach (var renderer in room.GetComponentsInChildren<MeshRenderer>())
        {
            var mesh = renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
            var materials = renderer.sharedMaterials;
            if (!renderer.enabled || moving.Contains(renderer.transform)) { Skip("動く物"); continue; }
            if (!GameObjectUtility.AnyStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI)) { Skip("Static でない"); continue; }
            if (mesh == null || materials.Length != mesh.subMeshCount) { Skip("メッシュと素材の数が合わない"); continue; }
            if (renderer.localToWorldMatrix.determinant < 0f) { Skip("裏返し"); continue; }
            if (materials.Any(m => m == null || m.renderQueue >= (int)RenderQueue.Transparent)) { Skip("透明"); continue; }

            bool lightmapped = renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE;
            float size = exterior != null && renderer.transform.IsChildOf(exterior) ? ExteriorCellSize : CellSize;
            var c = renderer.bounds.center;
            var cell = new Vector3Int(Mathf.FloorToInt(c.x / size), lightmapped ? 0 : Mathf.FloorToInt(c.y / ProbeLitCellHeight), Mathf.FloorToInt(c.z / size));
            for (int i = 0; i < materials.Length; i++)
            {
                var key = (materials[i], lightmapped ? renderer.lightmapIndex : -1, cell, renderer.shadowCastingMode);
                if (!groups.TryGetValue(key, out var parts)) groups[key] = parts = new List<Part>();
                parts.Add(new Part { renderer = renderer, mesh = mesh, subMesh = i });
            }
            sources.Add(renderer);
        }

        var parent = new GameObject("Combined Meshes").transform;
        int lightmappedGroups = 0;
        foreach (var (key, parts) in groups)
        {
            var mesh = new Mesh { name = $"Combined_{key.material.name}_{key.lightmap}_{key.cell.x}_{key.cell.y}_{key.cell.z}" };
            if (parts.Sum(p => p.mesh.vertexCount) > 65535) mesh.indexFormat = IndexFormat.UInt32;
            var instances = parts.Select(p => new CombineInstance
            {
                mesh = p.mesh,
                subMeshIndex = p.subMesh,
                transform = p.renderer.localToWorldMatrix,
                lightmapScaleOffset = p.renderer.lightmapScaleOffset,
            }).ToArray();
            mesh.CombineMeshes(instances, true, true, key.lightmap >= 0);

            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var combined = go.AddComponent<MeshRenderer>();
            combined.sharedMaterial = key.material;
            combined.shadowCastingMode = key.shadows;
            combined.reflectionProbeUsage = parts[0].renderer.reflectionProbeUsage;
            if (key.lightmap >= 0)
            {
                combined.lightmapIndex = key.lightmap;
                combined.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                go.AddComponent<CombinedMeshLightmap>().lightmapIndex = key.lightmap;
                lightmappedGroups++;
            }
        }

        foreach (var renderer in sources)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Object.DestroyImmediate(renderer);
            if (filter != null) Object.DestroyImmediate(filter);
        }
        Debug.Log($"[VR] メッシュをまとめた: {sources.Count} 個の物（描画 {groups.Values.Sum(p => p.Count)} 回ぶん）→ {groups.Count} 個（ライトマップ {lightmappedGroups} / ライトプローブ {groups.Count - lightmappedGroups}）" +
                  $", まとめなかった物: {string.Join(", ", skipped.Select(kv => $"{kv.Key} {kv.Value}"))}");
        return groups.Count;
    }
}
