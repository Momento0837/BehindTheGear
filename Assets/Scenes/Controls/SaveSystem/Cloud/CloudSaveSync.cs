using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BehindTheGear.HideOut;
using UnityEngine;

public enum CloudSyncAction { Nothing, Upload, Download, AskUser, Failed }

/// <summary>What the player must choose between when this PC and the account disagree.</summary>
public sealed class CloudConflict
{
    public bool localExists, localUsable, cloudExists, cloudUsable;
    public DateTime localSavedUtc, cloudSavedUtc;
    public string cloudDevice = "", localProblem = "", cloudProblem = "";
    public bool CanUseCloud => cloudUsable;
    public bool CanUseLocal => localUsable;
}

/// <summary>
/// Mirrors the existing bed save (slot-1-v2.json) to the signed-in account's Cloud Save data.
/// The local file stays the single source the game loads from: the cloud copy is only uploaded
/// after a successful bed save, and downloaded into the local slot (with backup) at the menu.
///
/// Direction is decided by content hashes, not clocks: local hash, cloud hash, and the hash this
/// PC last synchronized for the account. Ambiguous cases are never resolved automatically.
/// </summary>
public static class CloudSaveSync
{
    private const string CloudSaveBase = "https://cloud-save.services.api.unity.com/v1/data";
    private const int MaxPayloadBytes = 4 * 1024 * 1024; // Same ceiling as ManualSaveStore.Read.

    [Serializable] public sealed class CloudSlotValue
    {
        public int schema = 1;
        public int formatVersion;
        public string payload;
        public string sha256;
        public long savedAtUnixMs;
        public string device;
    }
    // Same field order/shape as ManualSaveStore's private envelope so written files are byte-compatible.
    [Serializable] private sealed class Envelope { public int formatVersion; public string payload, sha256; }
    [Serializable] private sealed class Item { public string key; public CloudSlotValue value; public string writeLock; }
    [Serializable] private sealed class ItemsResponse { public Item[] results; }
    [Serializable] private sealed class SetResponse { public string writeLock; }
    [Serializable] private sealed class SyncRecord { public int version = 1; public string userId; public string lastSyncedSha256; }

    private sealed class LocalSlot
    {
        public bool exists, usable;
        public string payload, sha256, problem = "";
        public DateTime savedUtc;
    }
    private sealed class CloudSlot
    {
        public bool exists, usable;
        public CloudSlotValue value;
        public string writeLock, problem = "";
        public DateTime SavedUtc => value != null && value.savedAtUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(value.savedAtUnixMs).UtcDateTime : DateTime.MinValue;
    }
    private enum Mode { Auto, AfterBedSave, ForceUpload, ForceDownload }

    public static bool Busy { get; private set; }
    public static string StatusText { get; private set; } = "";
    public static CloudConflict PendingConflict { get; private set; }
    public static event Action Changed;

    private static bool queuedBedUpload;
    private static HideOutPlayerBridge queuedBridge;
    private static string observedUserId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Busy = false; StatusText = ""; PendingConflict = null;
        queuedBedUpload = false; queuedBridge = null; observedUserId = null;
        CloudRunner.Lost -= RunnerLost; CloudRunner.Lost += RunnerLost;
        CloudAccountService.Changed -= AccountChanged; CloudAccountService.Changed += AccountChanged;
    }

    private static void RunnerLost()
    {
        if (!Busy) return;
        Busy = false; queuedBedUpload = false; queuedBridge = null;
        SetStatus("동기화가 중단되었습니다. 메인 메뉴에서 다시 동기화됩니다.");
    }

    private static void AccountChanged()
    {
        if (observedUserId == CloudAccountService.UserId) return;
        observedUserId = CloudAccountService.UserId;
        PendingConflict = null;
        StatusText = CloudAccountService.SignedIn ? "" : "로그인하면 다른 PC에서도 진행을 이어갈 수 있습니다.";
        Changed?.Invoke();
    }

    // ---------- Paths (computed independently from ManualSaveStore so validation slots are never synced) ----------
    private static string SaveRoot => Path.Combine(Application.persistentDataPath, "HideOutPrototype");
    public static string ProductionSlotPath => Path.Combine(SaveRoot, "slot-1-v2.json");
    private static string ProductionLegacyPath => Path.Combine(SaveRoot, "player-v1.json");
    private static string CloudDir => Path.Combine(Application.persistentDataPath, "CloudAccount");
    private static string BackupDir => Path.Combine(CloudDir, "backups");

    /// <summary>False while editor checks redirect ManualSaveStore into project Temp.</summary>
    public static bool ProductionSlotActive => SamePath(ManualSaveStore.SlotPath, ProductionSlotPath);
    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---------- Pure decision rule (covered by Tools > Cloud Save > Run offline checks) ----------
    public static CloudSyncAction Decide(bool localExists, bool localUsable, string localSha,
        bool cloudExists, bool cloudUsable, string cloudSha, string lastSyncedSha)
    {
        if (!localExists && !cloudExists) return CloudSyncAction.Nothing;
        if (cloudExists && !cloudUsable) return localUsable ? CloudSyncAction.AskUser : CloudSyncAction.Nothing;
        if (localExists && !localUsable) return cloudExists ? CloudSyncAction.AskUser : CloudSyncAction.Nothing;
        // A new PC has no record: take the account save. A PC that synced before and now has no
        // local slot means the player deleted it here; never silently resurrect it.
        if (!localExists) return string.IsNullOrEmpty(lastSyncedSha) ? CloudSyncAction.Download : CloudSyncAction.AskUser;
        // Account data missing although this PC synced it before means it was deleted elsewhere.
        if (!cloudExists) return string.IsNullOrEmpty(lastSyncedSha) ? CloudSyncAction.Upload : CloudSyncAction.AskUser;
        if (localSha == cloudSha) return CloudSyncAction.Nothing;
        if (!string.IsNullOrEmpty(lastSyncedSha))
        {
            if (localSha == lastSyncedSha) return CloudSyncAction.Download; // Only the account moved on.
            if (cloudSha == lastSyncedSha) return CloudSyncAction.Upload;   // Only this PC moved on.
        }
        return CloudSyncAction.AskUser;
    }

    // ---------- Public entry points ----------
    /// <summary>Menu sync: resolves the unambiguous cases and leaves a PendingConflict otherwise.</summary>
    public static void SyncNow(Action<CloudSyncAction, string> done = null) => Start(Mode.Auto, done);

    /// <summary>The player's explicit choice for a PendingConflict.</summary>
    public static void ResolveConflict(bool useCloud, Action<CloudSyncAction, string> done = null)
        => Start(useCloud ? Mode.ForceDownload : Mode.ForceUpload, done);

    /// <summary>Called once after HideOutPlayerBridge.Save() has replaced the local slot.</summary>
    public static void UploadAfterBedSave(HideOutPlayerBridge bridge)
    {
        // The local save already succeeded; nothing here may turn that into a reported failure.
        try { UploadAfterBedSaveUnsafe(bridge); }
        catch (Exception e) { Debug.LogWarning("[Cloud Save] 계정 업로드를 시작하지 못했습니다: " + e.Message); }
    }

    private static void UploadAfterBedSaveUnsafe(HideOutPlayerBridge bridge)
    {
        if (!CloudAccountService.SignedIn || !ProductionSlotActive || bridge == null || !SamePath(bridge.SavePath, ProductionSlotPath)) return;
        // The player only sees "저장 완료"; the upload runs quietly and speaks up only when it did not happen.
        if (Busy) { queuedBedUpload = true; queuedBridge = bridge; return; }
        Start(Mode.AfterBedSave, (action, message) =>
        {
            if (bridge == null || action == CloudSyncAction.Upload || action == CloudSyncAction.Nothing) return;
            bridge.SetMessage(action == CloudSyncAction.AskUser ? "저장 완료 · 계정 확인 필요" : "저장 완료 · 계정 업로드 실패");
            if (action == CloudSyncAction.Failed) Debug.LogWarning("[Cloud Save] 계정 업로드 실패: " + message);
        });
    }

    /// <summary>Removes the account copy (used together with the menu's local slot deletion).</summary>
    public static void DeleteCloudSave(Action<bool, string> done)
    {
        if (!CloudAccountService.SignedIn) { done?.Invoke(true, null); return; }
        if (Busy) { done?.Invoke(false, "계정 동기화 중에는 삭제할 수 없습니다."); return; }
        if (!ProductionSlotActive) { done?.Invoke(false, "검사 모드에서는 계정 저장을 건드리지 않습니다."); return; }
        Busy = true; SetStatus("계정 저장을 삭제하는 중...");
        CloudRunner.Run(DeleteRoutine((ok, message) =>
        {
            Busy = false;
            if (ok) { PendingConflict = null; WriteRecord(CloudAccountService.UserId, null); }
            SetStatus(ok ? "계정 저장을 삭제했습니다." : "계정 저장 삭제 실패: " + message);
            done?.Invoke(ok, message);
            RunQueuedUpload();
        }));
    }

    /// <summary>"나중에" in the conflict dialog: keep playing with this PC's save, decide later.</summary>
    public static void PostponeConflict()
    {
        if (PendingConflict == null) return;
        SetStatus("계정과 이 PC의 진행이 다릅니다. 계정 화면에서 선택할 수 있습니다.");
    }

    // ---------- Orchestration ----------
    private static void Start(Mode mode, Action<CloudSyncAction, string> done)
    {
        if (!CloudAccountService.SignedIn) { done?.Invoke(CloudSyncAction.Failed, "계정에 로그인되어 있지 않습니다."); return; }
        if (!ProductionSlotActive) { done?.Invoke(CloudSyncAction.Failed, "검사 모드에서는 계정 동기화를 하지 않습니다."); return; }
        if (Busy) { done?.Invoke(CloudSyncAction.Failed, "이미 동기화 중입니다."); return; }
        Busy = true;
        SetStatus(mode == Mode.AfterBedSave ? "계정에 올리는 중..." : "계정과 동기화하는 중...");
        CloudRunner.Run(SyncRoutine(mode, (action, message) =>
        {
            Busy = false;
            SetStatus(message);
            done?.Invoke(action, message);
            RunQueuedUpload();
        }));
    }

    private static void RunQueuedUpload()
    {
        if (!queuedBedUpload) return;
        var bridge = queuedBridge; queuedBedUpload = false; queuedBridge = null;
        if (bridge != null) UploadAfterBedSave(bridge);
    }

    private static IEnumerator SyncRoutine(Mode mode, Action<CloudSyncAction, string> done)
    {
        string token = null, tokenError = null;
        yield return CloudAccountService.WithIdToken((t, e) => { token = t; tokenError = e; });
        if (token == null) { done(CloudSyncAction.Failed, tokenError); yield break; }
        string userId = CloudAccountService.UserId;

        CloudSlot cloud = null; string fetchError = null;
        yield return FetchCloud(token, userId, (c, e) => { cloud = c; fetchError = e; });
        if (cloud == null)
        {
            done(CloudSyncAction.Failed, fetchError + (mode == Mode.AfterBedSave ? "" : " · 이 PC 저장으로 계속할 수 있습니다."));
            yield break;
        }

        var local = ReadLocal();
        string lastSynced = ReadRecord(userId);
        CloudSyncAction action = mode switch
        {
            Mode.ForceUpload => CloudSyncAction.Upload,
            Mode.ForceDownload => CloudSyncAction.Download,
            _ => Decide(local.exists, local.usable, local.sha256, cloud.exists, cloud.usable, cloud.value?.sha256, lastSynced),
        };
        // The game never swaps the loaded save under a running hideout: in-game it only uploads.
        if (mode == Mode.AfterBedSave && action == CloudSyncAction.Download) action = CloudSyncAction.AskUser;

        switch (action)
        {
            case CloudSyncAction.Nothing:
                PendingConflict = null;
                if (local.usable && cloud.usable && local.sha256 == cloud.value.sha256) WriteRecord(userId, local.sha256);
                done(CloudSyncAction.Nothing, DescribeSteady(local, cloud));
                yield break;

            case CloudSyncAction.AskUser:
                PendingConflict = new CloudConflict
                {
                    localExists = local.exists, localUsable = local.usable, localSavedUtc = local.savedUtc, localProblem = local.problem,
                    cloudExists = cloud.exists, cloudUsable = cloud.usable, cloudSavedUtc = cloud.SavedUtc,
                    cloudDevice = cloud.value?.device ?? "", cloudProblem = cloud.problem,
                };
                done(CloudSyncAction.AskUser, "이 PC와 계정의 진행이 서로 다릅니다. 사용할 기록을 선택하세요.");
                yield break;

            case CloudSyncAction.Download:
                if (!cloud.usable) { done(CloudSyncAction.Failed, cloud.exists ? cloud.problem : "계정에 저장된 진행이 없습니다."); yield break; }
                string downloadError = WriteDownloadedSlot(cloud.value);
                if (downloadError != null) { done(CloudSyncAction.Failed, downloadError); yield break; }
                WriteRecord(userId, cloud.value.sha256);
                PendingConflict = null;
                done(CloudSyncAction.Download, $"계정의 진행을 이 PC로 내려받았습니다 ({FormatLocal(cloud.SavedUtc)} 저장).");
                yield break;

            case CloudSyncAction.Upload:
                if (!local.usable) { done(CloudSyncAction.Failed, local.exists ? local.problem : "이 PC에 올릴 저장이 없습니다."); yield break; }
                var value = new CloudSlotValue
                {
                    formatVersion = 2, payload = local.payload, sha256 = local.sha256,
                    savedAtUnixMs = new DateTimeOffset(local.savedUtc).ToUnixTimeMilliseconds(),
                    device = SafeDeviceName(),
                };
                bool uploaded = false; bool conflicted = false; string uploadError = null;
                yield return PutCloud(token, userId, value, cloud.exists ? cloud.writeLock : null,
                    (ok, conflict, e) => { uploaded = ok; conflicted = conflict; uploadError = e; });
                if (conflicted)
                {
                    // Another PC wrote between our read and write: re-evaluate next time, never overwrite blindly.
                    PendingConflict = new CloudConflict
                    {
                        localExists = true, localUsable = true, localSavedUtc = local.savedUtc,
                        cloudExists = true, cloudUsable = false, cloudProblem = "방금 다른 PC에서 계정 저장이 바뀌었습니다. 다시 동기화해 주세요.",
                    };
                    done(CloudSyncAction.AskUser, "다른 PC에서 방금 계정 저장이 바뀌었습니다. 다시 동기화해 주세요.");
                    yield break;
                }
                if (!uploaded) { done(CloudSyncAction.Failed, uploadError); yield break; }
                WriteRecord(userId, local.sha256);
                PendingConflict = null;
                done(CloudSyncAction.Upload, $"이 PC의 진행을 계정에 올렸습니다 ({FormatLocal(local.savedUtc)} 저장).");
                yield break;
        }
        done(CloudSyncAction.Failed, "알 수 없는 동기화 상태입니다.");
    }

    private static string DescribeSteady(LocalSlot local, CloudSlot cloud)
    {
        if (local.usable && cloud.usable && local.sha256 == cloud.value.sha256)
            return $"동기화 완료 · 마지막 기록 {FormatLocal(cloud.SavedUtc)}";
        if (!local.exists && !cloud.exists) return "계정에 저장된 진행이 아직 없습니다. 세이브 포인트에서 저장하면 올라갑니다.";
        if (cloud.exists && !cloud.usable) return cloud.problem;
        if (local.exists && !local.usable) return local.problem;
        return "동기화 완료";
    }

    // ---------- Cloud Save REST ----------
    private static string ItemsUrl(string userId)
        => $"{CloudSaveBase}/projects/{Uri.EscapeDataString(CloudSaveConfig.ProjectId)}/players/{Uri.EscapeDataString(userId)}/items";

    private static IEnumerator FetchCloud(string token, string userId, Action<CloudSlot, string> done)
    {
        string key = CloudSaveConfig.SlotKey;
        CloudHttp.Response response = null;
        yield return CloudHttp.Send("GET", ItemsUrl(userId) + "?keys=" + Uri.EscapeDataString(key), null, token, null, r => response = r);
        var slot = new CloudSlot();
        if (response.status == 404 && !response.networkError) { done(slot, null); yield break; } // No data for this player yet.
        if (!response.Ok) { done(null, "계정 저장을 확인하지 못했습니다: " + CloudHttp.DescribeCommonFailure(response)); yield break; }
        ItemsResponse items;
        try { items = JsonUtility.FromJson<ItemsResponse>(response.body); }
        catch (Exception) { items = null; }
        if (items == null) { done(null, "계정 저장 응답을 해석할 수 없습니다."); yield break; }
        var item = items.results?.FirstOrDefault(i => i != null && i.key == key);
        if (item == null) { done(slot, null); yield break; }
        slot.exists = true; slot.value = item.value; slot.writeLock = item.writeLock;
        slot.problem = ValidateCloudValue(item.value);
        slot.usable = slot.problem == null;
        slot.problem ??= "";
        done(slot, null);
    }

    private static IEnumerator PutCloud(string token, string userId, CloudSlotValue value, string writeLock,
        Action<bool, bool, string> done)
    {
        // writeLock must be omitted when creating the item (JsonUtility would write an empty string).
        string body = "{\"key\":\"" + CloudHttp.Escape(CloudSaveConfig.SlotKey) + "\",\"value\":" + JsonUtility.ToJson(value)
            + (string.IsNullOrEmpty(writeLock) ? "" : ",\"writeLock\":\"" + CloudHttp.Escape(writeLock) + "\"") + "}";
        CloudHttp.Response response = null;
        yield return CloudHttp.Send("POST", ItemsUrl(userId), body, token, null, r => response = r);
        if (response.status == 409) { done(false, true, null); yield break; }
        if (!response.Ok) { done(false, false, CloudHttp.DescribeCommonFailure(response)); yield break; }
        SetResponse result = null;
        try { result = JsonUtility.FromJson<SetResponse>(response.body); } catch (Exception) { }
        done(result != null && !string.IsNullOrEmpty(result.writeLock), false,
            result == null ? "업로드 응답을 해석할 수 없습니다." : null);
    }

    private static IEnumerator DeleteRoutine(Action<bool, string> done)
    {
        string token = null, tokenError = null;
        yield return CloudAccountService.WithIdToken((t, e) => { token = t; tokenError = e; });
        if (token == null) { done(false, tokenError); yield break; }
        CloudHttp.Response response = null;
        yield return CloudHttp.Send("DELETE", ItemsUrl(CloudAccountService.UserId) + "/" + Uri.EscapeDataString(CloudSaveConfig.SlotKey),
            null, token, null, r => response = r);
        if (response.Ok || response.status == 404 && !response.networkError) { done(true, null); yield break; }
        done(false, CloudHttp.DescribeCommonFailure(response));
    }

    public static string ValidateCloudValue(CloudSlotValue value)
    {
        if (value == null || value.schema != 1) return "지원하지 않는 계정 저장 형식입니다.";
        if (value.formatVersion != 2) return "지원하지 않는 계정 저장 버전입니다.";
        if (string.IsNullOrEmpty(value.payload) || Encoding.UTF8.GetByteCount(value.payload) > MaxPayloadBytes)
            return "계정 저장 내용이 비어 있거나 너무 큽니다.";
        if (value.sha256 != Hash(value.payload)) return "계정 저장이 손상되었습니다. (무결성 검사 실패)";
        return null;
    }

    // ---------- Local slot ----------
    private static LocalSlot ReadLocal()
    {
        var slot = new LocalSlot();
        try
        {
            if (File.Exists(ProductionSlotPath))
            {
                slot.exists = true;
                slot.savedUtc = File.GetLastWriteTimeUtc(ProductionSlotPath);
                if (new FileInfo(ProductionSlotPath).Length > MaxPayloadBytes + 64 * 1024)
                { slot.problem = "이 PC의 저장 파일이 너무 큽니다."; return slot; }
                var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(ProductionSlotPath, Encoding.UTF8));
                if (envelope == null || envelope.formatVersion != 2 || string.IsNullOrEmpty(envelope.payload) || envelope.sha256 != Hash(envelope.payload))
                { slot.problem = "이 PC의 저장 파일이 손상되었거나 지원하지 않는 버전입니다."; return slot; }
                slot.payload = envelope.payload; slot.sha256 = envelope.sha256; slot.usable = true;
                return slot;
            }
            if (File.Exists(ProductionLegacyPath))
            {
                slot.exists = true;
                slot.savedUtc = File.GetLastWriteTimeUtc(ProductionLegacyPath);
                slot.problem = "이전 형식(v1) 저장입니다. 세이브 포인트에서 한 번 저장하면 계정에 올릴 수 있습니다.";
            }
        }
        catch (Exception e) { slot.usable = false; slot.problem = "이 PC의 저장을 읽지 못했습니다: " + e.Message; }
        return slot;
    }

    /// <summary>Validates fully, backs up the current slot, then atomically replaces it. Returns an error or null.</summary>
    private static string WriteDownloadedSlot(CloudSlotValue value)
    {
        string problem = ValidateCloudValue(value);
        if (problem != null) return problem;
        string json = JsonUtility.ToJson(new Envelope { formatVersion = 2, payload = value.payload, sha256 = value.sha256 }, true);
        string staging = Path.Combine(CloudDir, "download-check.json");
        try
        {
            Directory.CreateDirectory(CloudDir);
            File.WriteAllText(staging, json, Encoding.UTF8);
            // Same reader and validator as Continue, so a slot that cannot be continued is never installed.
            var save = ManualSaveStore.Read(staging);
            if (SaveGameSession.Settings != null) HideOutPlayerBridge.ValidateSnapshot(save, SaveGameSession.Settings);
        }
        catch (Exception e) { return "계정 저장이 현재 게임과 맞지 않아 내려받지 않았습니다: " + e.Message; }
        finally { TryDelete(staging); }

        string temporary = ProductionSlotPath + ".cloud.tmp";
        try
        {
            Directory.CreateDirectory(SaveRoot);
            BackupCurrentSlot();
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // Never delete-then-move: a failed replacement must leave the current slot intact.
            if (File.Exists(ProductionSlotPath)) File.Replace(temporary, ProductionSlotPath, ProductionSlotPath + ".bak");
            else File.Move(temporary, ProductionSlotPath);
            return null;
        }
        catch (Exception e) { return "내려받은 저장을 이 PC에 쓰지 못했습니다 (기존 저장 유지): " + e.Message; }
        finally { TryDelete(temporary); }
    }

    private static void BackupCurrentSlot()
    {
        if (!File.Exists(ProductionSlotPath)) return;
        Directory.CreateDirectory(BackupDir);
        File.Copy(ProductionSlotPath, Path.Combine(BackupDir, $"slot-before-cloud-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
        foreach (var old in new DirectoryInfo(BackupDir).GetFiles("slot-before-cloud-*.json")
                     .OrderByDescending(f => f.Name).Skip(5))
            TryDelete(old.FullName);
    }

    // ---------- Per-account sync record on this PC ----------
    private static string RecordPath(string userId)
    {
        var safe = new StringBuilder();
        foreach (char c in userId ?? "") safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return Path.Combine(CloudDir, "sync-" + safe + ".json");
    }

    private static string ReadRecord(string userId)
    {
        try
        {
            string path = RecordPath(userId);
            if (!File.Exists(path)) return null;
            var record = JsonUtility.FromJson<SyncRecord>(File.ReadAllText(path, Encoding.UTF8));
            return record != null && record.version == 1 && record.userId == userId ? record.lastSyncedSha256 : null;
        }
        catch (Exception) { return null; }
    }

    private static void WriteRecord(string userId, string sha)
    {
        if (string.IsNullOrEmpty(userId)) return;
        try
        {
            Directory.CreateDirectory(CloudDir);
            string path = RecordPath(userId);
            if (string.IsNullOrEmpty(sha)) { TryDelete(path); return; }
            File.WriteAllText(path, JsonUtility.ToJson(new SyncRecord { userId = userId, lastSyncedSha256 = sha }), Encoding.UTF8);
        }
        catch (Exception e) { Debug.LogWarning("[Cloud Save] 동기화 기록을 저장하지 못했습니다: " + e.Message); }
    }

    // ---------- Helpers ----------
    public static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string SafeDeviceName()
    {
        string name = SystemInfo.deviceName;
        if (string.IsNullOrEmpty(name) || name == SystemInfo.unsupportedIdentifier) return "다른 PC";
        return name.Length > 40 ? name.Substring(0, 40) : name;
    }

    public static string FormatLocal(DateTime utc)
        => utc == DateTime.MinValue ? "시간 정보 없음" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { /* Leftover temp/backup files are harmless and never auto-loaded. */ }
    }

    private static void SetStatus(string text)
    {
        StatusText = text ?? "";
        Changed?.Invoke();
    }
}
