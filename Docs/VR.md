# VR 版と画面版（Meta Quest / Mac・PC）

ブランチ `vr-setup` で、等身大の一人称で学校の部屋を歩き、窓の外でバンジージャンプをする体験を、**VR（Meta Quest 単体）**と**画面（キーボード・マウス・ゲームパッド）**の両方で遊べるようにした。中身（部屋・窓・バンジー・音）は同じで、プレイヤーの体と操作だけが違う。小人の試作（`Prototype.unity`、作者 Haru さんの元のゲーム）はそのまま残している。

## 方針

- VR: Meta Quest 3 / 3S / 2（Android アプリ）。Unity 公式の **OpenXR + XR Interaction Toolkit（XRI）**
- 画面版: Mac（ほかの PC も同じシーンで作れる）。XR は使わない
- 視点: どちらも**等身大の一人称**。遊んでいる途中で**ミニキャラ**（身長 25cm）にも切り替えられる（下の「ミニキャラモード」）

## VR と画面版の分け方

```
Assets/Scripts/
  Game/      共通（VR でも画面でも同じゲームの仕組み）。BanjiRun.Game.asmdef で XR を参照できないようにしてある
    PlayerBody.cs            体の窓口（足・頭・速さ、バンジー中に止めて外から動かす、移す、等身大 ⇔ ミニキャラ）。VR 版と画面版がこれを継ぐ
    ClimbAssist.cs           ミニキャラのとき、段に向かって歩くとよじ登る
    IUsable.cs               手で使える物（ドア・窓・ハーネス）の共通の形
    FirstPersonControls.cs   キーボード・マウス・ゲームパッドの一人称操作（画面版はいつも、VR 版はヘッドセットが無いときだけ）
    BungeeJump.cs, BungeeHarness.cs, CombinedMeshLightmap.cs
  VR/        VR だけ（XRI・OpenXR を使う）
    VRPlayerBody.cs          XR Origin の体（移動は XRI のスティック・テレポート。ミニキャラは XR Origin ごと縮める）
    VRUseOnGrab.cs           コントローラーでつかんだら IUsable を使う
    VRNoHeadsetFallback.cs   ヘッドセットなしで VR シーンを再生したら、キーボード・マウスで動けるようにする（試す・録画する用）
    Editor/                  VRSceneBuilder（VR シーン）・VRProjectSetup（Quest の設定と APK）・VRSceneStats
  Flat/      画面版だけ
    FlatPlayerBody.cs        CharacterController の体と目の高さのカメラ
    FlatHUD.cs               照準・使える物の案内・操作説明（H キーで出し入れ）
    FlatCameraEffects.cs     落ちる速さで視野が広がる・ロープが張る／着地で画面が揺れる（VR では酔うので使わない）
    Editor/                  FlatSceneBuilder（画面版のシーンと Mac アプリ）
  Editor/    共通のシーン作り: GameSceneBuilder（部屋 + バンジー + 軽量化 + ベイク）・BungeeBuilder・SceneOptimizer・SceneMeshCombiner・BungeeDemoRecorder
  Demo/      BungeeDemoDirector（自動操作で録画。エディター専用）
  Prototype/ 小人の試作。Door は IUsable にした（VR・画面版で使えるように）
```

- シーンは 2 つ: `VR_Prototype.unity`（XR Origin）と `Flat_Prototype.unity`（画面版のプレイヤー）。どちらも `GameSceneBuilder` が同じ部屋とバンジーを作り、プレイヤーだけを `VRSceneBuilder` / `FlatSceneBuilder` が置く
- VR だけの物（テレポート先・つかむ仕組み・ビネット）は `VRSceneBuilder` が足す。画面だけの物（照準・視野・揺れ）は `FlatSceneBuilder` が足す
- ビルドもシーンを分ける: Quest の APK は VR シーンだけ、Mac アプリは画面版のシーンだけ（ビルド設定の一覧に頼らない）。ビルド設定の一覧は VR が先頭でチェックあり、画面版と小人の試作はチェックなし
- 音・素材・模様は `Assets/Bungee/`（両方で使う）

## Unity の版

**6000.7.0b2（Unity 6.7 ベータ）に上げた。** 元は 6000.6.5f1 だったが、作業した Mac に 6.6 がなかったため。チームで開く人は全員 6000.7.0b2 に、**Android Build Support（OpenJDK・SDK & NDK 込み）付き**でそろえる必要がある（画面版の Mac アプリは Mac Build Support）。

上げたときに Unity が自動で変えたもの: URP 17.6 → 17.7、Input System 1.20 → 6.7、Test Framework・Timeline・uGUI など。XRI は 6.7 に合わせて 3.7.0-pre.1 になった。

## そのほかに入れたもの

| 場所 | 中身 |
| --- | --- |
| `Assets/Scenes/VR_Prototype/`, `Assets/Scenes/Flat_Prototype/` | それぞれのシーンのベイク結果（ライトマップ・反射プローブ・オクルージョン。シーンを作り直すと作り直される） |
| `Assets/Settings/VR_Lighting.lighting` | ベイクの設定（両方のシーンで使う） |
| `Assets/Samples/XR Interaction Toolkit/` | XRI のサンプル（Starter Assets・XR Interaction Simulator） |
| `Assets/XR/`, `Assets/XRI/` | XR Plug-in Management・OpenXR・XRI の設定 |
| `Assets/Bungee/Audio/` | バンジーの音（出どころは `CREDITS.txt`） |
| `Assets/Bungee/Materials/`, `Assets/Bungee/Textures/` | バンジーの台・クッション・格子の床などの素材（シーンを作ると作られる） |
| `Assets/Bungee/Models/Harness/` | 全身用ハーネスの 3D モデル（Tripo で生成。Pro プラン・非公開・商用利用可。約 2.1 万三角形、テクスチャは Quest 向けに 1024 まで） |
| `Assets/Fonts/MPLUSRounded1c-Bold.ttf` | 案内板と画面版の表示の日本語フォント（M PLUS Rounded 1c、SIL Open Font License 1.1） |

`PrototypeSceneBuilder.cs` は、部屋を作る部分を `CreateRoom()` として切り出し、VR・画面版のシーンからも使えるようにした（小人の試作の動きは同じ）。ホール東の窓（`EastWin_Sash_02`）をドアの一覧に足した。

## メニュー

**BanjiRun/Build All Scenes (VR + 画面版)** — 2 つのシーンを両方作り直す（ベイク込みで 5 分ほど）

**BanjiRun/VR**
1. **Import XRI Samples** — XRI のサンプルを取り込む（済み）
2. **Setup Quest Project** — Quest 用の設定（済み。何度実行してもよい）
3. **Build VR Scene** — `VR_Prototype.unity` を作り直す
4. **Build Quest APK** — `Builds/Quest/BanjiRun.apk` を作る（VR シーンだけ）
5. **Scene Stats** — VR シーンの重さ（ライト・コライダー・描画の回数）を測り、決まった 9 か所から見た画像を `Builds/SceneStats/latest/` に保存する（Quest の画質 `quest_*.png` とパソコンの画質 `pc_*.png`）。軽量化の前後を比べる用
6. **Record Bungee Demo** — VR シーンで、窓を開けてバンジーを飛び、戻るまでを自動で操作して `Recordings/BungeeDemo.mp4` に録画する（ffmpeg が要る。git には入らない）
7. **Record Mini Bungee Demo** — 同じ流れをミニキャラで（`Recordings/BungeeDemo_Mini_VR.mp4`）

**BanjiRun/画面版**
1. **Build Flat Scene** — `Flat_Prototype.unity` を作り直す
2. **Build Mac App** — `Builds/Mac/BanjiRun.app` を作る（画面版のシーンだけ）
3. **Record Bungee Demo** — 画面版で同じ流れを録画して `Recordings/BungeeDemo_Flat.mp4`
4. **Record Mini Bungee Demo** — 画面版でミニキャラの流れを録画して `Recordings/BungeeDemo_Mini_Flat.mp4`
5. **Build WebGL (unityroom)** — ブラウザ版を `Builds/WebGL/BanjiRun_日時/` に作る（下の「ブラウザ版（unityroom）」）

シーンを作るときに部屋のライトのベイクもするので、1 つ 1〜2 分かかる（M4 の Mac で約 1 分）。

## Quest 用の設定（2. でやること）

- Android: パッケージ名 `com.DefaultCompany.BanjiRun`（仮）、最低 API 32、IL2CPP、ARM64、Vulkan のみ、横向き（左）、ASTC、Game Activity
- XR Plug-in Management: **Android だけ** OpenXR を有効（Mac のエディターでは XR を起動しない）
- OpenXR（Android）: Meta Quest Support、Oculus Touch / Meta Quest Touch Plus のコントローラー、Single Pass Instanced（マルチビュー）
- 画質（Android は Mobile 段階 = `Mobile_RPAsset`）: 解像度 1.0、MSAA 4x、HDR オフ、1 つの物に効く毎フレームのライトは 2 個まで、影は 15m まで
- 静的バッチ オン（部屋の物は SceneMeshCombiner がまとめるので、それ以外の Static の物に効く）、MeshCollider の下ごしらえをビルド時に済ませる（Prebake Collision Meshes）
- XRI: Interaction Layer 31 を「Teleport」に。XR Interaction Simulator は自動では出さない（ヘッドセットなしの VR シーンは `VRNoHeadsetFallback` のキーボード・マウス操作）

## 軽量化（シーンを作るときにやること。もとは Quest 向け）

部屋は `PrototypeSceneBuilder.CreateRoom()` で置いたあと、`SceneOptimizer` で次のようにする（VR 版・画面版とも同じ）。小人の試作（`Prototype.unity`）は変えていない（.blend の取り込みでライトマップ用の UV を作るようにしたが、見た目は同じ）。

- **ライトをすべてベイク**: 78 個のライトを Baked にし、明るさをライトマップに焼き込む（毎フレーム計算するライトは 0 個）。ベイクは Progressive GPU、1m あたり 25 画素、方向なし、AO あり。ドア・手などの動く物と小さな物（30cm 未満）はライトプローブ（272 点）で、つやのある物には反射プローブ（ホール・入口・別室の 3 個）で部屋を映す
- **明るさ**: ベイクすると壁や天井に跳ね返った光も入るので、室内ライトの倍率（`RoomLightDimmer`）を VR だけ 0.3 → 0.1 にしている（`SceneOptimizer.BakedBrightness`）。ベイクしたライトは再生中に倍率を変えても明るさが変わらないので、変えたら 3. を実行し直す
- **Static**: ドア以外を Static にする（ライトマップ・オクルージョン用）。ガラスは「隠す物」にしない
- **メッシュをまとめる**（`SceneMeshCombiner`。シーンのファイルは変えず、ビルド時とエディターで再生するときに動く）: 部屋の Static の物を「素材 × ライトマップ × 4m 四方の升目」（ライトプローブで照らす小物は高さ 1.5m ごと、窓の外の街は 40m）ごとに 1 つのメッシュにまとめる。約 1,300 個 → 約 340 個。Unity の静的バッチは SRP Batcher と一緒だと 1 物 1 回の描画のままなので、代わりにこれで描画の回数を減らす（部屋の物には静的バッチの印を付けない。先に静的バッチが掛かるとまとめられなくなる）。ドア・ガラスなど透明な物はまとめない。コライダーは元の物に残る。まとめた物は升目ごとに描くので、見えない所の三角形も少し描く
- **オクルージョンカリング**: 壁の向こうの物を描かない
- **コライダー**: 10cm 未満の飾りと、下端が 2m より上の物（天井まわり・照明）からは外す。天井の照明（`Grp_Lighting` のライトレールとスポット）は長くても付けない（舞台の上で頭がつかえて歩けなくなるため）。残りは箱（BoxCollider）にする。壁・床のように元から箱の形の物は形どおり、それ以外は外形の箱。3m 以上で箱の形でない物（梁など 31 個）だけ MeshCollider のまま

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

**画面版**: `Flat_Prototype.unity` を開いて再生するか、`Builds/Mac/BanjiRun.app` を開く。Game ビューをクリックすると操作が始まる（操作は画面左上。H で消せる）。

**VR シーンをヘッドセットなしで**: `VR_Prototype.unity` を開いて再生すると、ヘッドセットが無いので `VRNoHeadsetFallback` が `FirstPersonControls` のキーボード・マウス操作に切り替える。WASD/矢印で移動、マウスで見回す（Game ビューをクリックするとカーソルが固定、Esc で解除）、Shift で走る、Space でジャンプ、E キーか左クリックで「使う」（見ている窓・ドア・ハーネス。届く距離は 2.5m）。Mac は Quest Link に対応していないので、実機の映像をエディターに出すことはできない。

**Quest 実機で**: Quest を開発者モードにして USB でつなぎ、Build Profiles（Android）から Build And Run。または 4. で APK を作り、Meta Quest Developer Hub か `adb install -r Builds/Quest/BanjiRun.apk` で入れる（adb は Unity の Android モジュールの `SDK/platform-tools/adb`）。Quest の中では「アプリ → 提供元不明」に BanjiRun が出る。

## ブラウザ版（unityroom）

画面版のシーンを WebGL でビルドし、unityroom に**非公開**で上げてある: https://unityroom.com/games/banjirun （2026-10-18。投稿者のいのべえさんだけが開ける）。

- ビルド: 画面版の 5.（バッチなら `-buildTarget WebGL -executeMethod FlatSceneBuilder.BuildWebGL`）。unityroom の決まりに合わせて Gzip・解凍のフォールバックなし・WebGL2・ファイル名はハッシュにしない、表示 960×540。初回はテクスチャの読み直しとシェーダーで 20 分ほど。約 35MB（data 26MB・wasm 10MB）
- 画質はブラウザ向けの Mobile 段階（Quest と同じ）。XR は使わない（XR Plug-in Management の WebGL は空）
- 上げ方は LG の作品と同じ（`LG-Template/Docs/unityroom.md` の「AI が上げる時」）: `Build/` の 4 ファイルを 9MB ずつ切ってページの中でつなぎ直し、SHA-256 を比べてから受け口に渡す
- unityroom の設定: Unity のバージョンは **6000.6.x**（6.7 は一覧にない。6.6 で動くのを確かめた）、表示 960×540・アスペクト比を維持、メモリ 512MB、紹介文と操作方法を入れた。アイコン・使用アセットの欄はまだ
- ビルドすると Unity の対象が WebGL になる。そのあと Quest の APK を作るときは Android への切り替え（テクスチャの読み直し）で時間がかかる
- WebGL のビルドでは URP が `Mobile_RPAsset` などの「シェーダーの間引き」の設定を自動で書き換える（XR のキーワードを外すなど）。Quest 用の設定なので、その書き換えはコミットに入れない（`git checkout` で戻す）

## VR での操作（XRI の初期設定）

- 左スティック: 移動
- 右スティック 左右: 45° 回転 / 前に倒して離す: テレポート（床だけ。`VRSceneBuilder.TeleportFloors`）
- 右 A ボタン: ジャンプ
- B ボタン（右）・Y ボタン（左）: ミニキャラ ⇔ 等身大
- グリップ: ドア・窓の開け閉め、ハーネスを付ける（触れるか、レイを当てて）
- 動いている間は視界の端が暗くなる（酔い対策のビネット）

## 画面版での操作

| | キーボード・マウス | ゲームパッド |
| --- | --- | --- |
| 移動 | WASD / 矢印（Shift で走る） | 左スティック（押し込みで走る） |
| 見回す | マウス（クリックでカーソル固定、Esc で解除） | 右スティック |
| ジャンプ | Space | A |
| 使う（窓・ハーネス） | E / 左クリック | X / 右トリガー |
| ミニキャラ ⇔ 等身大 | M | Y |
| 操作説明の表示 | H | — |

照準を窓やハーネスに合わせると「E / クリック：開ける」のように出る。

## 窓の外のバンジージャンプ

ホール東の窓（南から 2 枚目、`EastWin_Sash_02`）はドアと同じく開け閉めできる。開けて外のバルコニーへ出ると、バンジージャンプの台がある。建物は通りから約 23m の高さ。

1. 窓を開ける（グリップ / E）。窓のとなりに案内板
2. バルコニーの階段（3 段）でジャンプ台に上がる。手すりより外は**格子の床**で、足元から下の通りが透けて見える
3. フックの全身用ハーネスをつかむ（グリップ / E）と、カチャッと体に付いてロープがつながる（下を見ると腰から胸にハーネスが見える）。ここから**心臓の音**が鳴り始める
4. 台の先の**手すりのない細い板**（幅 50cm・長さ 1.35m）へ進む。板は先に行くほどしなって揺れ、ギシギシきしむ。心臓の音は速く大きくなる
5. 板の先に立つと、目の前の空中に 3・2・1・「バンジー！」と出て、係の人の声で数える（押し出しはしない）
6. 板の先から落ちると、約 1.3 秒の自由落下（最高で時速 50km ほど）。風の音が大きくなり、まわりを風の筋が流れる。ロープ（長さ 13m のばね）は**クッションの約 1m 上**でピンと張って引き戻す
7. 2 回弾んだら、一番上でゆっくり降ろされる
8. 下の歩道のクッション（4m 四方・高さ 1.6m のエアマット）に着地すると、歓声と「ナイスジャンプ！」。クッションの上の光る輪に入ると、バルコニーの窓の前へ戻る

ハーネスを付けずに落ちても、クッションで受け止める。ロープの強さ・弾む回数・降ろす速さ・板のしなりは `BungeeJump` のインスペクター（`cordLength` `stiffness` `bouncesBeforeLowering` `lowerSpeed` `plankDip` など）で変えられる。台・板・クッションの位置は `BungeeBuilder` が部屋モデルのバルコニー・窓・道路から決める（3. で作り直される）。クッションの場所に止まっていた車（`Car_00`）は、3. で道路に沿って 6m ずらしている。

落ちている間は、体（`PlayerBody`）をスクリプトで動かし、ふつうの移動は止める（VR はスティック移動・重力・テレポート、画面版は FirstPersonControls）。画面版だけ、落ちる速さで視野が広がり、ロープが張った瞬間と着地で画面が揺れる（`FlatCameraEffects`）。**VR で 20m 落ちて地面すれすれで引き戻されるのはかなり酔いやすい**ので、実機で試して強さを決める必要がある（弱めるなら `cordLength` を短く・`stiffness` を上げる）。

**音**（`Assets/Bungee/Audio/`、出どころは `CREDITS.txt`）: 台のまわりの風・心臓の音・板のきしみ・カラビナ・カウントダウンの声・飛び出す音・落ちる間の風・ロープが張る音・クッションに着地する音・歓声。OtoLogic（CC BY 4.0。公開するときはクレジットに「OtoLogic」と書く）と、ElevenLabs で作った音（Creator プラン、商用利用可）。

**動画**: VR の 6. と画面版の 3.（ミニキャラは VR の 7. と画面版の 4.）で、1 人称の映像（右上に外から見た様子、下に字幕）と音を録画する。バッチモードでは Unity の音が鳴らないので、鳴らした音とループの音量をコマごとに記録し、あとで同じ音のファイルから重ねて映像とまとめる（録画中にスピーカーから音は出ない）。操作はキーボード・マウス操作と同じ入口（`FirstPersonControls`）を自動で動かすので、移動・当たり判定・窓・ハーネス・バンジーはゲームと同じ動き。自動操作は、歩く向きを顔の向きと別に決めて顔はなめらかに向け、入力は 1 コマ遅れで渡す（前は 2 コマ遅れで一気に向き直していたので、歩く間ずっと映像が左右に細かく揺れていた）。ロープが張る瞬間と着地の揺れは画面版の演出（`FlatCameraEffects`）。

## ミニキャラモード

遊んでいる途中でいつでも（飛んでいる間を除く）、**身長 25cm のミニキャラ**に切り替えられる（大きさ `PlayerBody.MiniScale` = 0.15。元の小人の試作と同じくらい）。建物や台はそのままなので、同じ 23m の落差が、ミニの目には 150m ほどに見える。

- 切り替え: 画面版は M キー・ゲームパッドの Y、VR は B・Y ボタン。0.8 秒で縮む・伸びる（音は OtoLogic）。机の下など、頭の上がふさがっている所では等身大に戻れない（画面版は「ここでは大きくなれない」と出る）
- VR: XR Origin ごと縮める（`VRPlayerBody.ApplyScale`）。自分の頭や手の動きもそのまま小さくなる。XRI はスティックの移動の速さに XR Origin の大きさを掛けるので、遅くなりすぎないよう大きさの平方根の速さに戻している
- 画面版: CharacterController の寸法と目の高さを縮める（`FlatPlayerBody.ApplyScale`）
- 速さ・手の届く距離: 大きさの平方根で縮める（そのままだと歩くのが遅すぎ、窓やハーネスに届かない）。ジャンプの高さは大きさどおり
- **よじ登り**（`ClimbAssist`）: ミニの体は舞台（20cm）・窓のさん・ジャンプ台の階段（1 段 29cm）をそのまま上がれないので、段に向かって歩くと 0.55 秒で上に乗る。登れるのは約 35cm まで（机やいすの座面には登れない）
- **バンジー**: 腰の高さ・体に付けたハーネスを縮める。ロープはミニ用の長さ 15m・強さ 3.3（`miniCordLength` `miniStiffness`）で、最初の一番下がクッションの約 30cm 上（ミニの体 1 つ分）になる。ロープの体側の端は細くし、目の前を太い線が横切らないよう 12cm 後ろにつなぐ
- クッションの当たり判定は、弾む見た目（`Cushion`）と分けて動かさない（`Cushion_Collider`）。面が上下すると、ミニの小さな体がクッションに沈んで下に抜けていたため
- ロープの張りや揺れ、着地の画面の揺れ（画面版）は等身大と同じ

## まだやっていないこと・気になる点

- **重さ**: ライトのベイク・メッシュのまとめ・オクルージョン・コライダーの簡略化はした（上の「Quest 向けの軽量化」）。描画の回数は最大 325 回。残る手は、遠くの小物を消す（LOD）、Quest の固定中心窩レンダリング（Foveated Rendering。画面の端を粗く描いて GPU を軽くする）。まとめたメッシュにライトマップが正しく付くかは、エディターでは確かめたが、実機ではまだ
- **実機での確認**: APK のビルドまで。Quest で動かして、フレームレート・酔い・大きさの感じは見ていない（描画の回数はエディターで測った目安）。ミニキャラの VR（XR Origin ごと縮める・B/Y ボタン・よじ登り）は、ヘッドセットなしのキーボード操作でしか確かめていない
- **ミニキャラの案内**: VR では切り替えのボタン（B・Y）をどこにも表示していない。案内板か手元の表示が要る
- **ゲームの中身**: 試作のダッシュ・ジャンプ・E キーのドアは、デスクトップ版のまま。VR での遊び（目的、敵、UI）はこれから。操作説明が画面の文字（OnGUI）なので、VR ではワールド空間の UI にする必要がある
- **出現位置**: VR 版はホールの床から始まる。モデルには `PlayerSpawn_Elevator`（エレベーター内の出現位置）と `TitleCamera_Elevator` があるので、タイトル画面やスタート地点に使える
- **OpenXR の検証で残った警告**: PC 用の Renderer（`PC_Renderer`）の SSAO。Android は Mobile 段階しか使わないので Quest には影響しないはず
- パッケージ名 `com.DefaultCompany.BanjiRun` は仮。ストアに出す前に決める
