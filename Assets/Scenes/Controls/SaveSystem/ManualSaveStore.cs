using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BehindTheGear.HideOut;
using UnityEngine;

/// <summary>One manual slot. A pending new game never touches the old slot.</summary>
public static class ManualSaveStore
{
    [Serializable] private sealed class Envelope
    {
        public int formatVersion;
        public string payload, sha256;
    }
    private static string Root => validationRoot ?? Path.Combine(Application.persistentDataPath, "HideOutPrototype");
    private static string validationRoot;
    public static string SlotPath => Path.Combine(Root, "slot-1-v2.json");
    public static string LegacyPath => Path.Combine(Root, "player-v1.json");
#if UNITY_EDITOR
    public static void UseValidationDirectory(string directory)
    {
        string temp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp")) + Path.DirectorySeparatorChar;
        if (directory != null && !Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Validation slots must remain inside project Temp.");
        validationRoot = directory;
    }
#endif
    public static bool Exists => File.Exists(SlotPath) || File.Exists(LegacyPath);
    public static HideOutSave ReadSlot() => Read(File.Exists(SlotPath) ? SlotPath : LegacyPath);
    public static HideOutSave Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("빈 슬롯입니다. 새 게임을 시작하세요.");
        string json = File.ReadAllText(path, Encoding.UTF8);
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("저장 파일이 너무 큽니다.");
        if (json.Contains("\"formatVersion\""))
        {
            var envelope = JsonUtility.FromJson<Envelope>(json);
            if (envelope == null || envelope.formatVersion != 2) throw new InvalidDataException("지원하지 않는 저장 버전입니다.");
            if (string.IsNullOrEmpty(envelope.payload) || envelope.sha256 != Hash(envelope.payload))
                throw new InvalidDataException("손상된 저장 파일입니다. 원본을 유지합니다.");
            var save = JsonUtility.FromJson<HideOutSave>(envelope.payload);
            if (save == null || save.version != 2) throw new InvalidDataException("지원하지 않는 저장 데이터 버전입니다.");
            return save;
        }
        // The original v1 envelope was the snapshot itself. Never normalize a broken file into a new game.
        if (!json.Contains("\"version\"") || !json.Contains("\"inventory\"") || !json.Contains("\"levels\""))
            throw new InvalidDataException("손상된 저장 파일입니다.");
        var legacy = JsonUtility.FromJson<HideOutSave>(json);
        if (legacy == null || legacy.version != 1) throw new InvalidDataException("지원하지 않는 저장 버전입니다.");
        return legacy;
    }
    public static void Write(string path, HideOutSave save)
    {
        string payload = JsonUtility.ToJson(save);
        string json = JsonUtility.ToJson(new Envelope { formatVersion = 2, payload = payload, sha256 = Hash(payload) }, true);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // Do not fall back to delete-then-move: replacement failure must preserve the old file.
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void DeleteSlot()
    {
        // Backups are not auto-loaded, but also remove legacy files so deleting cannot resurrect this slot.
        foreach (string path in new[] { SlotPath, LegacyPath })
            foreach (string suffix in new[] { ".tmp", ".bak", "" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
