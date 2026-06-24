# Obfuscator Tool

Unity Tool for compiling selected runtime C# scripts into an obfuscated DLL and replacing the selected source files with that DLL after a verified backup.

## What It Does

Script Obfuscator lets you choose a single script, folder, multiple source paths, or a Unity project root, then:

1. Resolves valid runtime `.cs` sources
2. Compiles them into a DLL with `dotnet`
3. Obfuscates the DLL with ConfuserEx
4. Prompts for a backup destination after success
5. Copies the original source files and `.meta` files into a timestamped backup folder
6. Replaces the selected sources with the obfuscated DLL at the selected location

## Main Features

- Unity Editor window at `Tools > Script Obfuscator`
- Scene-based UI Toolkit demo interface
- Single script, folder, drag-and-drop, and path-based source selection
- Support for both project-local `Assets/...` paths and absolute external paths
- Recursive source expansion with automatic `Editor` folder exclusion
- asmdef-aware expansion for project-local sources
- Post-build backup workflow with user-selected destination
- Protection presets:
  - Basic: rename
  - Balanced: rename + constants
  - Strong: rename + constants + control flow
  - Custom: manual toggle combination
- Integration and EditMode tests for key workflows

## Tech Stack

- Unity `6000.3.16f1`
- C#
- Unity UI Toolkit
- Unity Editor APIs
- .NET SDK
- ConfuserEx
- NUnit / Unity Test Framework



## Setup

1. Open the project in Unity `6000.3.16f1`.
2. Install the .NET SDK if it is not already available on your machine.
3. Download ConfuserEx from [the official releases page](https://github.com/mkaring/ConfuserEx/releases/latest).
4. Extract the release so this file exists:

```text
Tools/ConfuserEx/Confuser.CLI.exe
```

## Usage

1. Open `Tools > Script Obfuscator`.
2. Add a runtime script, folder, external folder, or Unity project root.
3. Set the DLL name and target framework.
4. Choose the desired protection level.
5. Click `Build Obfuscated DLL`.
6. After a successful build, choose where the original source backup should be saved.

The tool then creates a timestamped `*.SourceBackup~` folder, copies the original sources and `.meta` files into it, writes the obfuscated DLL into the selected source location, and removes the original selected source files.

## Validation and Test Coverage

The project includes tests for:

- source normalization
- duplicate removal
- recursive folder expansion
- `Editor` folder exclusion
- Unity project root scanning
- external project reference resolution
- protection XML generation
- full replacement and backup flows
- backup cancel behavior

## Important Limitation

Replacing Unity `.cs` files with a DLL does not automatically preserve scene or prefab references for `MonoBehaviour` and `ScriptableObject` types. Unity serializes references against the original script asset and `.meta` identity. A wrapper or bridge layer is required if seamless reference preservation is a hard requirement.

## Current Positioning

This project is best used as a Unity editor obfuscation and packaging tool for protecting source code and shipping proprietary runtime logic as a DLL. It is not yet a fully transparent Unity component replacement system.
