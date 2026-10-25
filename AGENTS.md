# Unity project rules

- The team development version is Unity 6.3 LTS, exactly **6000.3.23f1**.
- Never change the Unity Editor version without the user's explicit permission. Compilation, validation, tests, and batch execution must use 6000.3.23f1 only.
- Before launching Unity, verify the executable's actual version. If 6000.3.23f1 is unavailable, report it; never substitute another version.
- Changing only ProjectSettings/ProjectVersion.txt does not resolve compatibility issues.
- Do not upgrade/downgrade packages, change the render pipeline, reinstall packages, delete Library, or perform broad reimports without explaining the evidence and obtaining approval.
- Preserve all existing tracked and untracked work. Back up existing changes in any file before editing it. Do not reset, clean, discard, or overwrite teammates' work.
- Do not change branches, pull, merge, rebase, commit, or push unless explicitly requested. Report conflicts without choosing a side.
- If this project is already open, use the existing editor. Do not start a second editor, delete locks, or forcibly terminate Unity.
- Keep fixes minimal; preserve existing fonts, material presets, Korean text, UI layout, and game logic. Never edit Library/PackageCache.
- Report only verification actually performed, and clearly identify remaining manual checks.
