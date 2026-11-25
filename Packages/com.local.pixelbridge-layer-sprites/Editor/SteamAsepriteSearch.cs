using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PixelBridge.LayerSprites
{
    internal static class SteamAsepriteSearch
    {
        // Valve KeyValues stores quoted keys/values; numeric library entries also occur in older Steam clients.
        public static IEnumerable<KeyValuePair<string,string>> Values(string text)
        {
            foreach(Match match in Regex.Matches(text ?? "", "\"((?:\\\\.|[^\"\\\\])*)\"\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
                yield return new KeyValuePair<string,string>(Unescape(match.Groups[1].Value),Unescape(match.Groups[2].Value));
        }
        private static string Unescape(string value) { return value.Replace("\\\\","\\").Replace("\\\"","\""); }
        public static IEnumerable<string> LibraryPaths(string text)
        {
            foreach(var pair in Values(text))
                if((pair.Key=="path" || int.TryParse(pair.Key,out _)) && Path.IsPathRooted(pair.Value)) yield return pair.Value;
        }
        public static string Find(IEnumerable<string> steamRoots)
        {
            var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string root in steamRoots)
            {
                if(string.IsNullOrEmpty(root)) continue;
                libraries.Add(root);
                foreach(string relative in new[]{"steamapps/libraryfolders.vdf","config/libraryfolders.vdf"})
                    foreach(string path in LibraryPaths(Read(Path.Combine(root,relative)))) libraries.Add(path);
            }
            foreach(string library in libraries)
            {
                string common=Path.Combine(library,"steamapps","common");
                foreach(var pair in Values(Read(Path.Combine(library,"steamapps","appmanifest_431730.acf"))))
                {
                    if(pair.Key!="installdir" || string.IsNullOrWhiteSpace(pair.Value)) continue;
                    string directory=Path.GetFullPath(Path.Combine(common,pair.Value));
                    if(!directory.StartsWith(Path.GetFullPath(common)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) continue;
                    string found=Executable(directory);if(found!=null) return found;
                }
                string fallback=Executable(Path.Combine(common,"Aseprite"));if(fallback!=null) return fallback;
            }
            return null;
        }
        private static string Read(string path)
        {
            try { return File.Exists(path)?File.ReadAllText(path):""; }
            catch(IOException) { return ""; }
            catch(UnauthorizedAccessException) { return ""; }
        }
        private static string Executable(string folder)
        {
            foreach(string relative in new[]{"Aseprite.exe","aseprite.exe","aseprite","Aseprite.app/Contents/MacOS/aseprite"})
            {
                string path=Path.Combine(folder,relative);if(File.Exists(path)) return Path.GetFullPath(path);
            }
            return null;
        }
    }
}
