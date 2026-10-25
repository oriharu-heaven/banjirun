#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// エディター専用。画面版シーン（Flat_Prototype）で再生し、実際の移動（CharacterController）と操作（FirstPersonControls）だけで
/// 「① エレベーター内で E を押して扉を開ける → ② エレベーターからロビーへ歩いて出る → ③ 入口ドアを開けて教室へ入る →
///  ④ 教室を歩いて窓を開けバルコニーへ出る → ⑤ ハーネスを付けてバンジーで飛び降りる → ⑥ 地面のクッションに着地してばんじーの画像が出る」
/// の全工程が引っかからずに通ることを検証する。
/// </summary>
[DefaultExecutionOrder(1000)]
public class TimeAttackPlaytestRunner : MonoBehaviour
{
    PlayerBody body;
    FirstPersonControls controls;
    BungeeJump bungee;
    TimeAttackManager manager;
    Camera cam;
    FirstPersonControls.Commands commands;
    bool finished;

    IEnumerator Start()
    {
        Time.timeScale = 4f; // 検証を速く回す
        Application.runInBackground = true;
        yield return null;

        manager = FindFirstObjectByType<TimeAttackManager>();
        body = FindFirstObjectByType<PlayerBody>();
        controls = FindFirstObjectByType<FirstPersonControls>();
        bungee = FindFirstObjectByType<BungeeJump>();
        if (manager == null || body == null || controls == null || bungee == null)
        {
            Finish(false, "必要なコンポーネントが見つかりません");
            yield break;
        }

        controls.Scripted = true;
        cam = body.Camera;

        // 1. エレベーター内配置の確認 & Eキー（Use）で扉を開ける
        float spawnDist = Vector3.Distance(Flat(body.Feet), Flat(TimeAttackManager.ElevatorSpawn));
        Debug.Log($"[WalkTest] 1. エレベーター内スタート確認: feet={body.Feet:F2}, waiting={manager.IsElevatorWaiting}");
        if (!manager.IsElevatorWaiting || spawnDist > 0.35f)
        {
            Finish(false, $"エレベーター初期位置または状態が不正: feet={body.Feet:F2}, waiting={manager.IsElevatorWaiting}");
            yield break;
        }

        yield return LookAt(new Vector3(-7.75f, 1.4f, 1.95f), 0.3f);
        yield return Use();
        yield return Wait(1.1f);
        if (!manager.IsElevatorOpen)
        {
            Finish(false, "Eキー操作後にエレベーターの扉が開きませんでした");
            yield break;
        }
        Debug.Log("[WalkTest] 2. エレベーター扉オープン完了");

        // 2. エレベーターから歩いてロビー（Floor_Entrance）へ出る
        Vector3 lobbyPoint = new Vector3(-7.75f, 0.02f, 0.74f);
        yield return WalkTo(lobbyPoint, 8f);
        if (finished) yield break;
        Debug.Log($"[WalkTest] 3. エレベーターからロビーへ歩行成功: feet={body.Feet:F2}");

        // 3. 入口ドア（Door_Entrance）を開けて教室（Floor_Annex -> Floor_Hall）へ歩いて入る
        Transform doorLeaf = GameObject.Find("Door_Entrance_Leaf")?.transform;
        Vector3 doorCenter = doorLeaf != null ? doorLeaf.GetComponent<Renderer>().bounds.center : new Vector3(-6.63f, 1.0f, 0.74f);
        yield return LookAt(doorCenter, 0.4f);
        yield return Use();
        yield return Wait(0.8f);

        Vector3 insideEntrance = new Vector3(-5.55f, 0.02f, 0.74f);
        yield return WalkTo(insideEntrance, 8f);
        if (finished) yield break;
        Debug.Log($"[WalkTest] 4. 入口ドア通過成功: feet={body.Feet:F2}");

        // 4. 教室の中央通路を歩いて東の窓（EastWin_Sash_02）の前まで進む
        Vector3 hallCenter = new Vector3(-5.65f, 0.02f, -3.25f);
        Vector3 hallWest = new Vector3(-8.05f, 0.02f, -3.25f);
        Vector3 hallNearWindow = new Vector3(-8.05f, 0.02f, -2.30f);
        yield return WalkTo(hallCenter, 10f);
        if (finished) yield break;
        yield return WalkTo(hallWest, 10f);
        if (finished) yield break;
        yield return WalkTo(hallNearWindow, 8f);
        if (finished) yield break;

        Vector3 outward = bungee.jumpEdge.forward;
        Vector3 along = bungee.returnPoint.forward;
        Transform glass = GameObject.Find("EastWin_Sash_02_Glass")?.transform;
        Vector3 glassCenter = glass != null ? glass.GetComponent<Renderer>().bounds.center : bungee.returnPoint.position - outward;
        Vector3 floor = new Vector3(0f, 0.02f, 0f);
        Vector3 aisle = Flat(glassCenter - outward * 0.75f + along * 0.1f) + floor;
        Vector3 beforeStage = aisle - outward * 1.0f;
        Vector3 windowInside = Flat(glassCenter - outward * 0.75f + along * 0.28f) + floor;
        Vector3 balcony = bungee.returnPoint.position;
        Vector3 deck = Vector3.up * bungee.platformTop;

        yield return WalkTo(beforeStage, 10f);
        if (finished) yield break;
        yield return WalkTo(windowInside, 8f);
        if (finished) yield break;
        Debug.Log($"[WalkTest] 5. 教室を歩いて窓の前へ到達成功: feet={body.Feet:F2}");

        // 5. 窓を開けてバルコニーへ出て、階段を上がってハーネスを装着
        yield return LookAt(glassCenter, 0.5f);
        yield return Use();
        yield return Wait(1.0f);

        yield return WalkTo(Flat(windowInside + outward * 1.3f) + floor, 8f);
        if (finished) yield break;
        yield return WalkTo(balcony, 8f);
        if (finished) yield break;
        yield return WalkTo(Flat(balcony + along * 0.6f) + Vector3.up * balcony.y, 8f);
        if (finished) yield break;
        yield return WalkTo(Flat(balcony + along * 1.95f) + deck, 10f);
        if (finished) yield break;
        Vector3 topOfStairs = Flat(balcony + along * 1.95f + outward * 0.6f) + deck;
        yield return WalkTo(topOfStairs, 8f);
        if (finished) yield break;
        Debug.Log($"[WalkTest] 6. バンジー台へ到達成功: feet={body.Feet:F2}");

        yield return LookAt(bungee.harness.transform.position, 0.6f);
        yield return Use();
        yield return Wait(0.5f);
        if (bungee.CurrentPhase != BungeeJump.Phase.Harnessed)
        {
            Finish(false, $"ハーネスを装着できませんでした: phase={bungee.CurrentPhase}");
            yield break;
        }

        // 6. 板の先端へ歩いてバンジージャンプ → クッション着地でお祝い画像表示を確認
        float vMid = Vector3.Dot(bungee.jumpEdge.position - balcony, along);
        Vector3 onGrating = Flat(balcony + along * vMid + outward * 2.2f) + deck;
        Vector3 edge = bungee.jumpEdge.position - outward * 0.25f;
        yield return WalkTo(onGrating, 8f);
        yield return WalkTo(Flat(edge) + deck, 8f);

        for (float w = 0f; w < 5f && bungee.countdownSign.text != "バンジー！" && bungee.CurrentPhase == BungeeJump.Phase.Harnessed; w += Time.deltaTime)
            yield return null;

        float jt = 0f;
        while (bungee.CurrentPhase == BungeeJump.Phase.Harnessed && jt < 3f)
        {
            commands.move = Vector2.up;
            yield return LookStep(bungee.cushion.position, 4f);
            jt += Time.deltaTime;
        }
        commands = default;

        if (bungee.CurrentPhase != BungeeJump.Phase.Falling)
        {
            Finish(false, $"バンジー飛降りに失敗しました: phase={bungee.CurrentPhase}");
            yield break;
        }
        Debug.Log("[WalkTest] 7. バンジー飛降り成功！着地まで待機中…");

        float fallWait = 0f;
        while ((bungee.CurrentPhase == BungeeJump.Phase.Falling || bungee.CurrentPhase == BungeeJump.Phase.Lowering) && fallWait < 60f)
        {
            fallWait += Time.deltaTime;
            yield return null;
        }

        yield return null;
        bool landedOk = bungee.CurrentPhase == BungeeJump.Phase.Landed && manager.ShowBirthdayCard && manager.birthdayCard != null;
        Debug.Log($"[WalkTest] 8. クッション着地確認: phase={bungee.CurrentPhase}, ShowBirthdayCard={manager.ShowBirthdayCard}, boardActive={manager.birthdayCardBoard?.activeSelf}");
        Finish(landedOk, landedOk ? "エレベーター → 教室移動 → バンジー → 着地＆お祝い画像表示の全工程を確認完了" : "着地後のお祝い画像表示が不正");
    }

    Coroutine yield_WalkTo(Vector3 target, float timeout) => StartCoroutine(WalkTo(target, timeout));

    bool MoveSucceeded(Coroutine c, Vector3 target, string label) => true;

    void LateUpdate()
    {
        if (controls == null) return;
        controls.ScriptedCommands = commands;
        commands.use = false;
        commands.resize = false;
        commands.jump = false;
        commands.look = Vector2.zero;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    IEnumerator Use()
    {
        commands.use = true;
        yield return null;
        yield return null;
    }

    IEnumerator WalkTo(Vector3 target, float timeout = 10f)
    {
        float t = 0f;
        for (; t < timeout; t += Time.deltaTime)
        {
            if (Flat(target - body.Feet).magnitude < 0.16f || body.Locked) break;
            yield return WalkStep(target);
        }
        commands.move = Vector2.zero;
        yield return null;
        if (Flat(target - body.Feet).magnitude >= 0.35f && !body.Locked)
        {
            Finish(false, $"移動途中で引っかかりました: 現在地 {body.Feet:F2} → 目標 {target:F2}");
            yield break;
        }
    }

    IEnumerator WalkStep(Vector3 target)
    {
        Vector3 to = Flat(target - body.Feet);
        float distance = to.magnitude;
        Vector3 forward = Flat(body.transform.forward).normalized, right = Flat(body.transform.right).normalized;
        float yawError = distance > 1e-4f ? Vector3.SignedAngle(forward, to, Vector3.up) : 0f;
        if (distance > 0.25f)
        {
            float maxTurn = 180f * Time.deltaTime;
            commands.look.x = Mathf.Clamp(yawError * (1f - Mathf.Exp(-8f * Time.deltaTime)), -maxTurn, maxTurn);
        }
        commands.look.y = (CurrentPitch() - 8f) * 0.1f;
        Vector3 dir = distance > 1e-4f ? to / distance : Vector3.zero;
        commands.move = new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward));
        commands.run = true;
        yield return null;
    }

    IEnumerator LookAt(Vector3 target, float duration)
    {
        commands.move = Vector2.zero;
        for (float t = 0f; t < duration; t += Time.deltaTime) yield return LookStep(target, 10f);
    }

    IEnumerator LookStep(Vector3 target, float sharpness)
    {
        Vector3 dir = target - cam.transform.position;
        float yawError = Vector3.SignedAngle(Flat(body.transform.forward), Flat(dir), Vector3.up);
        float desiredPitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        float f = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        commands.look = new Vector2(yawError * f, (CurrentPitch() - Mathf.Clamp(desiredPitch, -80f, 80f)) * f);
        yield return null;
    }

    float CurrentPitch() => Mathf.DeltaAngle(0f, cam.transform.localEulerAngles.x);

    void Finish(bool ok, string msg)
    {
        if (finished) return;
        finished = true;
        Time.timeScale = 1f;
        Debug.Log($"[TimeAttackTest] {(ok ? "PASS" : "FAIL")}: {msg}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
#endif
