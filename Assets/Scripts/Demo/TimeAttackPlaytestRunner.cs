#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// エディター専用。画面版シーン（Flat_Prototype）で再生し、
/// 「スタート（カウントダウン）→ ゴール（計測停止・誕生日メッセージ）→ リトライ（スタートに戻る）」が通ることを自動で検証する。
/// </summary>
public class TimeAttackPlaytestRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return null;
        var manager = FindFirstObjectByType<TimeAttackManager>();
        var body = FindFirstObjectByType<PlayerBody>();
        if (manager == null || body == null)
        {
            Finish(false, "TimeAttackManager または PlayerBody が見つかりません");
            yield break;
        }

        // 1. スタート時のカウントダウン確認
        if (manager.CurrentState != TimeAttackManager.State.Countdown)
        {
            Finish(false, $"初期状態が Countdown ではありません: {manager.CurrentState}");
            yield break;
        }
        float startDist = Vector3.Distance(body.Feet, manager.startPoint.position);
        Debug.Log($"[TimeAttackTest] 1. スタート配置OK: feet={body.Feet:F2}, start={manager.startPoint.position:F2} (ずれ {startDist:F2}m), state={manager.CurrentState}");

        // カウントダウン終了（3秒）を待つ
        float wait = 0f;
        while (manager.CurrentState == TimeAttackManager.State.Countdown && wait < 5f)
        {
            wait += Time.deltaTime;
            yield return null;
        }
        if (manager.CurrentState != TimeAttackManager.State.Running)
        {
            Finish(false, $"カウントダウン後に Running になりませんでした: {manager.CurrentState}");
            yield break;
        }
        Debug.Log($"[TimeAttackTest] 2. カウントダウン完了 → 計測開始OK: state={manager.CurrentState}, locked={body.Locked}");

        // 少し時間を進めて計測中であることを確認
        yield return new WaitForSeconds(0.5f);
        if (manager.ElapsedTime <= 0.2f)
        {
            Finish(false, $"経過時間が増えていません: {manager.ElapsedTime:F2}");
            yield break;
        }

        // 2. ゴール地点へ移動して計測停止を確認
        body.TeleportFeetTo(manager.goalPoint.position, manager.goalPoint.eulerAngles.y);
        yield return null;
        yield return null;
        if (manager.CurrentState != TimeAttackManager.State.Cleared)
        {
            Finish(false, $"ゴール接触後に Cleared になりませんでした: {manager.CurrentState}");
            yield break;
        }
        Debug.Log($"[TimeAttackTest] 3. ゴール到達 → 計測停止OK: clearTime={TimeAttackManager.FormatTime(manager.ClearTime)}, sign='{manager.goalSign?.text.Replace("\n", " / ")}'");

        // 3. リトライでスタートに戻り、再びカウントダウンが始まることを確認
        yield return new WaitForSeconds(0.3f);
        manager.Retry();
        yield return null;
        float retryDist = Vector3.Distance(body.Feet, manager.startPoint.position);
        bool retryOk = manager.CurrentState == TimeAttackManager.State.Countdown && retryDist < 0.25f;
        Debug.Log($"[TimeAttackTest] 4. リトライ確認: state={manager.CurrentState}, スタート位置との差={retryDist:F2}m");

        Finish(retryOk, retryOk ? "スタート → ゴール → リトライの全工程を確認完了" : "リトライ後の状態または位置が不正");
    }

    void Finish(bool ok, string msg)
    {
        Debug.Log($"[TimeAttackTest] {(ok ? "PASS" : "FAIL")}: {msg}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
#endif
