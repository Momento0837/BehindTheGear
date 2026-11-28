using System.IO;

/// <summary>
/// Leftover of the removed quit auto-save. Progress is written to disk only at the bed, so closing the game no longer
/// saves anything. Older builds wrote "slot-1-autosave.json" next to the manual slot; the start menu, 새 게임 and 저장 삭제
/// delete that file so it can never be loaded.
/// </summary>
public static class QuitAutoSave
{
    public static string FilePath => Path.Combine(Path.GetDirectoryName(ManualSaveStore.SlotPath), "slot-1-autosave.json");
    public static bool Exists => File.Exists(FilePath);

    public static void Delete()
    {
        foreach (string suffix in new[] { "", ".tmp", ".bak" })
            if (File.Exists(FilePath + suffix)) File.Delete(FilePath + suffix);
    }
}
