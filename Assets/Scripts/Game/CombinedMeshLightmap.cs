using UnityEngine;

/// <summary>
/// ビルド時にまとめたメッシュ（SceneMeshCombiner）に、ライトマップを付け直す。
/// まとめたメッシュはライトのベイクの後に作るのでベイク結果（LightingData）に載っておらず、読み込み時に上書きされないよう自分で付ける。
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class CombinedMeshLightmap : MonoBehaviour
{
    public int lightmapIndex = -1;

    void Awake()
    {
        var meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.lightmapIndex = lightmapIndex;
        meshRenderer.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f); // UV はまとめるときにライトマップ上の位置へ直してある
    }
}
