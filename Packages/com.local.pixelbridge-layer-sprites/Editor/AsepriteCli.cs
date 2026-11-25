using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;

namespace PixelBridge.LayerSprites
{
    internal static class AsepriteCli
    {
        internal static string FindExecutable()
        {
            string manual = EditorPrefs.GetString(LayerSpriteMenu.Preference, "");
            if (File.Exists(manual)) return manual;
            var roots = new List<string>();
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string key in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
                {
                    try
                    {
                        using (var entry = hive.OpenSubKey(key))
                            foreach (string name in new[] { "SteamPath", "InstallPath" })
                                if (entry?.GetValue(name) is string path) roots.Add(path);
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            }
            else
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                roots.Add(Path.Combine(home, "Library/Application Support/Steam"));
                roots.Add(Path.Combine(home, ".local/share/Steam"));
                roots.Add(Path.Combine(home, ".steam/steam"));
            }
            return SteamAsepriteSearch.Find(roots);
        }

        private static string Quote(string argument)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in argument)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
                result.Append(ch);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        internal static void Export(string source, string script, string output)
        {
            string executable = FindExecutable();
            if (!File.Exists(executable))
                throw new FileNotFoundException("Aseprite 실행 파일을 지정한 뒤 Reimport하세요. " + LayerSpriteMenu.SetExecutableMenu);
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--batch --oneframe " + Quote(Path.GetFullPath(source)) +
                    " --script-param " + Quote("output=" + output) + " --script " + Quote(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new IOException("Aseprite를 시작할 수 없습니다.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    throw new TimeoutException("Aseprite 레이어 내보내기 시간이 초과되었습니다.");
                }
                if (process.ExitCode != 0 || !File.Exists(Path.Combine(output, "layers.txt")))
                    throw new IOException("Aseprite 내보내기 실패. " + stderr.GetAwaiter().GetResult() + stdout.GetAwaiter().GetResult());
            }
        }
    }
}
