using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Offline checks for the account cloud save (no network, no save files touched).</summary>
public static class CloudSaveChecks
{
    private static int passed, failed;
    private static readonly List<string> lines = new();

    [MenuItem("Tools/Cloud Save/Run offline checks")]
    public static void Run()
    {
        passed = failed = 0; lines.Clear();
        DecisionTable();
        CloudValueValidation();
        CredentialRules();
        Check(System.Text.RegularExpressions.Regex.IsMatch(CloudSaveConfig.SlotKey, "^[A-Za-z0-9_-]{1,255}$"), "slot key is a valid Cloud Save key");
        Check(!string.IsNullOrEmpty(CloudSaveConfig.ProjectId), "Unity Cloud project ID is available (Project Settings > Services)");

        string summary = $"[Cloud Save Checks] {passed} passed, {failed} failed";
        string report = summary + "\n" + string.Join("\n", lines);
        Directory.CreateDirectory("Temp/CloudSaveValidation");
        File.WriteAllText("Temp/CloudSaveValidation/offline-checks.txt", report);
        if (failed == 0) Debug.Log(report); else Debug.LogError(report);
    }

    [MenuItem("Tools/Cloud Save/Open account data folder")]
    public static void OpenFolder()
    {
        string path = Path.Combine(Application.persistentDataPath, "CloudAccount");
        Directory.CreateDirectory(path);
        EditorUtility.RevealInFinder(path);
    }

    private static void DecisionTable()
    {
        const string A = "hashA", B = "hashB", C = "hashC";
        Expect(false, false, null, false, false, null, null, CloudSyncAction.Nothing, "nothing anywhere");
        Expect(false, false, null, true, true, A, null, CloudSyncAction.Download, "new PC downloads the account save");
        Expect(false, false, null, true, true, A, A, CloudSyncAction.AskUser, "slot deleted on this PC is not resurrected silently");
        Expect(true, true, A, false, false, null, null, CloudSyncAction.Upload, "first link uploads this PC's save");
        Expect(true, true, A, false, false, null, A, CloudSyncAction.AskUser, "account copy deleted elsewhere asks");
        Expect(true, true, A, true, true, A, null, CloudSyncAction.Nothing, "identical saves need nothing");
        Expect(true, true, A, true, true, B, A, CloudSyncAction.Download, "only the account moved on: download");
        Expect(true, true, B, true, true, A, A, CloudSyncAction.Upload, "only this PC moved on: upload");
        Expect(true, true, B, true, true, C, A, CloudSyncAction.AskUser, "both moved on: ask");
        Expect(true, true, A, true, true, B, null, CloudSyncAction.AskUser, "unknown history with two saves: ask");
        Expect(true, false, null, true, true, A, null, CloudSyncAction.AskUser, "corrupt/legacy local with account save: ask");
        Expect(true, false, null, false, false, null, null, CloudSyncAction.Nothing, "corrupt/legacy local alone is left untouched");
        Expect(true, true, A, true, false, null, A, CloudSyncAction.AskUser, "corrupt account copy is never downloaded");
        Expect(false, false, null, true, false, null, null, CloudSyncAction.Nothing, "corrupt account copy alone: nothing");
    }

    private static void Expect(bool le, bool lu, string ls, bool ce, bool cu, string cs, string last, CloudSyncAction expected, string label)
    {
        var actual = CloudSaveSync.Decide(le, lu, ls, ce, cu, cs, last);
        Check(actual == expected, $"{label} (expected {expected}, got {actual})");
    }

    private static void CloudValueValidation()
    {
        string payload = "{\"version\":2}";
        var good = new CloudSaveSync.CloudSlotValue { formatVersion = 2, payload = payload, sha256 = CloudSaveSync.Hash(payload), savedAtUnixMs = 1 };
        Check(CloudSaveSync.ValidateCloudValue(good) == null, "valid cloud value accepted");
        var tampered = new CloudSaveSync.CloudSlotValue { formatVersion = 2, payload = payload + " ", sha256 = good.sha256 };
        Check(CloudSaveSync.ValidateCloudValue(tampered) != null, "tampered payload rejected by hash");
        var future = new CloudSaveSync.CloudSlotValue { formatVersion = 3, payload = payload, sha256 = good.sha256 };
        Check(CloudSaveSync.ValidateCloudValue(future) != null, "unsupported format version rejected");
        var schema = new CloudSaveSync.CloudSlotValue { schema = 2, formatVersion = 2, payload = payload, sha256 = good.sha256 };
        Check(CloudSaveSync.ValidateCloudValue(schema) != null, "unsupported cloud schema rejected");
        Check(CloudSaveSync.ValidateCloudValue(null) != null, "missing value rejected");
        string json = JsonUtility.ToJson(good);
        var roundTrip = JsonUtility.FromJson<CloudSaveSync.CloudSlotValue>(json);
        Check(roundTrip != null && CloudSaveSync.ValidateCloudValue(roundTrip) == null && roundTrip.payload == payload, "cloud value survives JSON round trip");
    }

    private static void CredentialRules()
    {
        Check(CloudAccountService.NormalizeUsername("  Eden_01 ") == "eden_01", "username trimmed and lower-cased");
        Check(CloudAccountService.ValidateUsername("ab") != null, "username shorter than 3 rejected");
        Check(CloudAccountService.ValidateUsername("eden 01") != null, "username with space rejected");
        Check(CloudAccountService.ValidateUsername("eden.player@gear") == null, "username with . @ accepted");
        Check(CloudAccountService.ValidatePassword("Gear2026!") == null, "strong password accepted");
        Check(CloudAccountService.ValidatePassword("gear2026!") != null, "password without uppercase rejected");
        Check(CloudAccountService.ValidatePassword("Gear!") != null, "short password rejected");
        Check(CloudAccountService.ValidatePassword("GearGear2026") != null, "password without symbol rejected");
    }

    private static void Check(bool condition, string label)
    {
        if (condition) passed++; else failed++;
        lines.Add((condition ? "PASS " : "FAIL ") + label);
    }
}
