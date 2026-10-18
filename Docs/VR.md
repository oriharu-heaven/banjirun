# VR 化の準備（Meta Quest 単体）

ブランチ `vr-setup` で、Meta Quest 単体（PC なし）で遊べる VR 版の土台を入れた。デスクトップ版の試作（`Prototype.unity`）はそのまま残している。

## 方針

- 対象: Meta Quest 3 / 3S / 2（Android アプリ）
- 部品: Unity 公式の **OpenXR + XR Interaction Toolkit（XRI）**
- 視点: **等身大の一人称**（自分の実際の背の高さで部屋を歩く）。小人サイズにしたくなったら `VRSceneBuilder.cs` の `PlayerScale` を下げる（0.15 で身長 25cm ほど。未検証）

## Unity の版

**6000.7.0b2（Unity 6.7 ベータ）に上げた。** 元は 6000.6.5f1 だったが、作業した Mac に 6.6 がなかったため。チームで開く人は全員 6000.7.0b2 に、**Android Build Support（OpenJDK・SDK & NDK 込み）付き**でそろえる必要がある。

上げたときに Unity が自動で変えたもの: URP 17.6 → 17.7、Input System 1.20 → 6.7、Test Framework・Timeline・uGUI など。XRI は 6.7 に合わせて 3.7.0-pre.1 になった。

## 追加したもの

| 場所 | 中身 |
| --- | --- |
| `Assets/Scripts/VR/VRDoorHandle.cs` | VR でドアを開け閉めする（ドアに触れるかレイを当ててグリップ） |
| `Assets/Scripts/VR/Editor/VRProjectSetup.cs` | Quest 用のプロジェクト設定・APK ビルド（メニュー） |
| `Assets/Scripts/VR/Editor/VRSceneBuilder.cs` | VR シーンの生成（メニュー） |
| `Assets/Scripts/VR/Editor/VRSceneOptimizer.cs` | VR シーンを Quest 向けに軽くする（ライトのベイク・Static・コライダー。3. の中で使う） |
| `Assets/Scripts/VR/Editor/VRMeshCombiner.cs` | ビルド時（とエディターで再生するとき）に、部屋の物を素材と場所ごとに 1 つのメッシュにまとめる |
| `Assets/Scripts/VR/CombinedMeshLightmap.cs` | まとめたメッシュにライトマップを付け直す（VRMeshCombiner が付ける） |
| `Assets/Scripts/VR/Editor/VRSceneStats.cs` | VR シーンの重さを測って、決まった場所からの画像を保存する（メニュー 5.） |
| `Assets/Scenes/VR_Prototype/` | VR シーンのベイク結果（ライトマップ・反射プローブ・オクルージョン。3. で作り直される） |
| `Assets/Settings/VR_Lighting.lighting` | ベイクの設定（3. で上書きされる） |
| `Assets/Scenes/VR_Prototype.unity` | 生成した VR シーン（試作と同じ部屋 + XR Origin） |
| `Assets/Samples/XR Interaction Toolkit/` | XRI のサンプル（Starter Assets・XR Interaction Simulator） |
| `Assets/XR/`, `Assets/XRI/` | XR Plug-in Management・OpenXR・XRI の設定 |
| `Assets/Scripts/VR/BungeeJump.cs`, `BungeeHarness.cs` | 窓の外のバンジージャンプ（下の「窓の外のバンジージャンプ」） |
| `Assets/Scripts/VR/Editor/BungeeBuilder.cs` | バンジーの台・ハーネス・ロープ・クッション・案内板を作る（3. の中で使う） |
| `Assets/Scripts/VR/VRDesktopControls.cs`, `VRPlayerBody.cs`, `IVRUsable.cs` | ヘッドセットが無いときのキーボード・マウス操作と、体の窓口（バンジー中に移動を止める等）、手で使える物の共通の形 |
| `Assets/Scripts/VR/Demo/BungeeDemoDirector.cs`, `Editor/BungeeDemoRecorder.cs` | バンジーの一連の流れを自動で操作して動画にする（6.） |
| `Assets/Fonts/MPLUSRounded1c-Bold.ttf` | 案内板の日本語フォント（M PLUS Rounded 1c、SIL Open Font License 1.1） |
| `Assets/VR/Materials/` | バンジーの台・クッションなどの素材（3. で作られる） |

`PrototypeSceneBuilder.cs` は、部屋を作る部分を `CreateRoom()` として切り出し、VR シーンからも使えるようにした（デスクトップ版の動きは同じ）。

## メニュー「BanjiRun/VR」

1. **Import XRI Samples** — XRI のサンプルを取り込む（済み）
2. **Setup Quest Project** — Quest 用の設定（済み。何度実行してもよい）
3. **Build VR Scene** — `VR_Prototype.unity` を作り直す（ビルドするシーンにも登録する）
4. **Build Quest APK** — `Builds/Quest/BanjiRun.apk` を作る
5. **Scene Stats** — VR シーンの重さ（ライト・コライダー・描画の回数）を測り、決まった 9 か所から見た画像を `Builds/SceneStats/latest/` に保存する（Quest の画質 `quest_*.png` とパソコンの画質 `pc_*.png`）。軽量化の前後を比べる用
6. **Record Bungee Demo** — 窓を開けてバンジーを飛び、戻るまでを自動で操作して `Recordings/BungeeDemo.mp4` に録画する（ffmpeg が要る。git には入らない）

3. は部屋のライトのベイクもするので、1〜2 分かかる（M4 の Mac で約 1 分）。

## Quest 用の設定（2. でやること）

- Android: パッケージ名 `com.DefaultCompany.BanjiRun`（仮）、最低 API 32、IL2CPP、ARM64、Vulkan のみ、横向き（左）、ASTC、Game Activity
- XR Plug-in Management: **Android だけ** OpenXR を有効（Mac のエディターでは XR を起動しない）
- OpenXR（Android）: Meta Quest Support、Oculus Touch / Meta Quest Touch Plus のコントローラー、Single Pass Instanced（マルチビュー）
- 画質（Android は Mobile 段階 = `Mobile_RPAsset`）: 解像度 1.0、MSAA 4x、HDR オフ、1 つの物に効く毎フレームのライトは 2 個まで、影は 15m まで
- 静的バッチ オン（部屋の物は VRMeshCombiner がまとめるので、それ以外の Static の物に効く）、MeshCollider の下ごしらえをビルド時に済ませる（Prebake Collision Meshes）
- XRI: Interaction Layer 31 を「Teleport」に。XR Interaction Simulator は自動では出さない（エディターではキーボード・マウス操作の `VRDesktopControls` を使う）

## Quest 向けの軽量化（3. でやること）

部屋は `PrototypeSceneBuilder.CreateRoom()` で置いたあと、`VRSceneOptimizer` で次のようにする。パソコン版の試作（`Prototype.unity`）は変えていない（.blend の取り込みでライトマップ用の UV を作るようにしたが、見た目は同じ）。

- **ライトをすべてベイク**: 78 個のライトを Baked にし、明るさをライトマップに焼き込む（毎フレーム計算するライトは 0 個）。ベイクは Progressive GPU、1m あたり 25 画素、方向なし、AO あり。ドア・手などの動く物と小さな物（30cm 未満）はライトプローブ（272 点）で、つやのある物には反射プローブ（ホール・入口・別室の 3 個）で部屋を映す
- **明るさ**: ベイクすると壁や天井に跳ね返った光も入るので、室内ライトの倍率（`RoomLightDimmer`）を VR だけ 0.3 → 0.1 にしている（`VRSceneOptimizer.BakedBrightness`）。ベイクしたライトは再生中に倍率を変えても明るさが変わらないので、変えたら 3. を実行し直す
- **Static**: ドア以外を Static にする（ライトマップ・オクルージョン用）。ガラスは「隠す物」にしない
- **メッシュをまとめる**（`VRMeshCombiner`。シーンのファイルは変えず、ビルド時とエディターで再生するときに動く）: 部屋の Static の物を「素材 × ライトマップ × 4m 四方の升目」（ライトプローブで照らす小物は高さ 1.5m ごと、窓の外の街は 40m）ごとに 1 つのメッシュにまとめる。約 1,300 個 → 約 340 個。Unity の静的バッチは SRP Batcher と一緒だと 1 物 1 回の描画のままなので、代わりにこれで描画の回数を減らす（部屋の物には静的バッチの印を付けない。先に静的バッチが掛かるとまとめられなくなる）。ドア・ガラスなど透明な物はまとめない。コライダーは元の物に残る。まとめた物は升目ごとに描くので、見えない所の三角形も少し描く
- **オクルージョンカリング**: 壁の向こうの物を描かない
- **コライダー**: 10cm 未満の飾りと、下端が 2m より上の物（天井まわり・照明）からは外す。残りは箱（BoxCollider）にする。壁・床のように元から箱の形の物は形どおり、それ以外は外形の箱。3m 以上で箱の形でない物（梁など 31 個）だけ MeshCollider のまま

### 変更前と変更後（2026-10-18、`5. Scene Stats` で測定）

| | 変更前 | ベイク・静的バッチ・オクルージョン | ＋ メッシュをまとめる |
| --- | --- | --- | --- |
| 毎フレーム計算するライト | 74 個（ほかに面光源 4 個。面光源はリアルタイムでは光らない） | 0 個（78 個すべてベイク） | 0 個 |
| コライダー | 1,339 個（MeshCollider 1,333） | 628 個（BoxCollider 596・MeshCollider 31） | 624 個（窓のドアが増えたぶん少し変わった） |
| 描画の回数（Draw Calls、9 か所の平均 / 最大） | 1,442 / 2,432 | 294 / 849 | 143 / 325 |
| SetPass（9 か所の平均） | 12 | 28 | 7 |
| 三角形（9 か所の平均） | 233,507 | 64,817 | 95,664 |

描画の回数は Mobile 画質・片目ぶん・頭と手を除いた数。変更前は太陽の影を描くためにほぼ全部の物を 2 回描いていた。いちばん多いのはホールの隅から部屋全体を見たとき（325）。メッシュをまとめても見た目はほぼ同じ（ライトプローブで照らす小物の明るさが、まとめた塊の真ん中の値になるので、プロジェクターの金具などが少し暗くなる程度）。

**見た目の違い**: 変更前は全体が青白く、白飛びが多かった。これは太陽の光が薄い天井・壁を通り抜けて部屋中を照らしていた（影の漏れ）のと、空の光（環境光）が室内にもそのまま当たっていたため。変更後は暖かい色になり、家具の柔らかい影・天井の照り返し・部屋の隅の暗さが入る。白飛びは減った。ライトの照り返し（つや）はなくなり、反射プローブの映り込みになる。動く物（ドア・手）は影を受けない。比べた画像は `Builds/SceneStats/` にある（git には入らない）。青白さを足す案（空の色の影なしライトを 6 方向から足す）も試したが、暖色の版にした。

## 試し方

**Mac のエディターで**: `VR_Prototype.unity` を開いて再生すると、ヘッドセットが無いので `VRDesktopControls` のキーボード・マウス操作になる。WASD/矢印で移動、マウスで見回す（Game ビューをクリックするとカーソルが固定、Esc で解除）、Shift で走る、Space でジャンプ、E キーか左クリックで「使う」（見ている窓・ドア・ハーネス。届く距離は 2.5m）。Mac は Quest Link に対応していないので、実機の映像をエディターに出すことはできない。

**Quest 実機で**: Quest を開発者モードにして USB でつなぎ、Build Profiles（Android）から Build And Run。または 4. で APK を作り、Meta Quest Developer Hub で入れる。

## VR での操作（XRI の初期設定）

- 左スティック: 移動
- 右スティック 左右: 45° 回転 / 前に倒して離す: テレポート（床だけ。`VRSceneBuilder.TeleportFloors`）
- 右 A ボタン: ジャンプ
- グリップ: ドア・窓の開け閉め、ハーネスを付ける（触れるか、レイを当てて）
- 動いている間は視界の端が暗くなる（酔い対策のビネット）

## 窓の外のバンジージャンプ

ホール東の窓（南から 2 枚目、`EastWin_Sash_02`）はドアと同じく開け閉めできる。開けて外のバルコニーへ出ると、バンジージャンプの台がある。建物は通りから約 23m の高さ。

1. 窓を開ける（グリップ / E）。窓のとなりに案内板
2. バルコニーの階段（3 段）でジャンプ台に上がる。台は手すりの外へ 1.8m 張り出している（高さ 1.15m）
3. フックのハーネスをつかむ（グリップ / E）と体に付き、ロープがつながる
4. 台の先に立つと、目の前の空中に 3・2・1・「バンジー！」と出る（押し出しはしない）
5. 台の先から歩いて（またはジャンプして）落ちると、ロープ（長さ 9m のばね）で 2 回弾み、そのあと一番上でゆっくり降ろされる
6. 下の歩道のクッション（4m 四方・高さ 1.6m のエアマット）に着地する。クッションの上の光る輪に入ると、バルコニーの窓の前へ戻る

ハーネスを付けずに落ちても、クッションで受け止める。ロープの強さ・弾む回数・降ろす速さは `BungeeJump` のインスペクター（`stiffness` `bouncesBeforeLowering` `lowerSpeed` など）で変えられる。台・クッションの位置は `BungeeBuilder` が部屋モデルのバルコニー・窓・道路から決める（3. で作り直される）。

落ちている間は、ヘッドセットでも体（XR Origin）をスクリプトで動かし、スティック移動・重力・テレポートは止める（`VRPlayerBody`）。VR で 20m 落ちて弾むのは酔いやすいので、実機で試して強さを決める必要がある。

**動画**: 6. で、1 人称の映像（右上に外から見た様子、下に字幕）を録画する。操作はキーボード・マウス操作と同じ入口（`VRDesktopControls`）を自動で動かすので、移動・当たり判定・窓・ハーネス・バンジーはゲームと同じ動き。

## まだやっていないこと・気になる点

- **重さ**: ライトのベイク・メッシュのまとめ・オクルージョン・コライダーの簡略化はした（上の「Quest 向けの軽量化」）。描画の回数は最大 325 回。残る手は、遠くの小物を消す（LOD）、Quest の固定中心窩レンダリング（Foveated Rendering。画面の端を粗く描いて GPU を軽くする）。まとめたメッシュにライトマップが正しく付くかは、エディターでは確かめたが、実機ではまだ
- **実機での確認**: APK のビルドまで。Quest で動かして、フレームレート・酔い・大きさの感じは見ていない（描画の回数はエディターで測った目安）
- **ゲームの中身**: 試作のダッシュ・ジャンプ・E キーのドアは、デスクトップ版のまま。VR での遊び（目的、敵、UI）はこれから。操作説明が画面の文字（OnGUI）なので、VR ではワールド空間の UI にする必要がある
- **出現位置**: VR 版はホールの床から始まる。モデルには `PlayerSpawn_Elevator`（エレベーター内の出現位置）と `TitleCamera_Elevator` があるので、タイトル画面やスタート地点に使える
- **OpenXR の検証で残った警告**: PC 用の Renderer（`PC_Renderer`）の SSAO。Android は Mobile 段階しか使わないので Quest には影響しないはず
- パッケージ名 `com.DefaultCompany.BanjiRun` は仮。ストアに出す前に決める
