# Script Obfuscator

Unity UI Toolkit editor tooling for compiling selected runtime C# scripts into a DLL and obfuscating that DLL with ConfuserEx.

## Setup

1. Install the .NET SDK.
2. Download ConfuserEx from https://github.com/mkaring/ConfuserEx/releases/latest.
3. Extract the release ZIP into `Tools/ConfuserEx` so this file exists:

```text
Tools/ConfuserEx/Confuser.CLI.exe
```

## Usage

1. Open `Tools > Script Obfuscator`.
2. Drag runtime `.cs` files or runtime folders into the script list.
3. Set the DLL name. The DLL is written beside the selected script, inside the selected folder, or inside `Assets` when a Unity project root is selected.
4. Choose `Rename`, `Control flow`, and/or `String encryption / constants`.
5. Click `Build Obfuscated DLL`.
6. After compilation and obfuscation succeed, choose where the original sources should be backed up.

The tool accepts any filesystem backup destination and creates a verified timestamped `SourceBackup~` folder containing the original source structure and `.meta` files. Unity ignores the `~` folder when it is placed inside `Assets`. The tool then places the obfuscated DLL at the selected source location and removes only the source files included in that DLL. Canceling the backup picker leaves all originals untouched. Editor folders are excluded from runtime DLL replacement. Anti-tamper and anti-dump are intentionally not exposed because they are risky for Unity assembly loading and IL2CPP builds.

For Unity Asset Store packages, keep public wrappers, inspectors, and editor scripts readable as `.cs` files. Ship proprietary runtime implementation as a precompiled obfuscated DLL.

The obfuscation pipeline uses `UnityEditor`, `AssetDatabase`, and external process APIs, so it is implemented as an editor-only UI Toolkit window rather than a player-runtime MonoBehaviour. Source cleanup can back up expanded source files outside `Assets` after a successful DLL build.
