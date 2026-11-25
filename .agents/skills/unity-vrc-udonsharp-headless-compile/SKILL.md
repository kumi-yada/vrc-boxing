---
name: unity-vrc-udonsharp-headless-compile
description: >-
    Recompile UdonSharp programs without opening the Unity editor UI, by running the
    Unity editor in batch mode (-batchmode -nographics) with -executeMethod calling
    UdonSharpCompilerV1.CompileSync(). Use when asked to compile UdonSharp from the
    command line, in CI, after editing .cs files outside the editor, "recompile the
    programs headlessly", or when the editor GUI is unavailable. Triggers: batchmode,
    headless compile, -executeMethod, CompileSync, SerializedUdonPrograms, recompile
    UdonSharp assets, unity -batchmode, compile without editor.
license: MIT
metadata:
    version: "1.0.0"
    tags: vrchat, udonsharp, unity, batchmode, headless, compile, ci, vcc
---

# Headless UdonSharp compilation

## Why this exists

UdonSharp's compiler (`UdonSharpCompilerV1`, in `Packages/com.vrchat.worlds/Integrations/UdonSharp/Editor/...`)
lives in editor-only assemblies and depends on `UnityEditor` APIs, so it **cannot** be run with plain
`dotnet build` / `csc` / `msbuild`. The supported way to recompile without the editor UI is to run the
Unity editor in **batch mode** and invoke the compiler via `-executeMethod`.

## Workflow

### 1. Entry-point script

`Assets/Editor/UdonSharpHeadlessCompile.cs` (already present in this project) exposes a static method
for `-executeMethod`. If it is missing in another project, recreate it exactly as:

```csharp
#if UNITY_EDITOR
using System;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;

public static class UdonSharpHeadlessCompile
{
    public static void CompileUdonSharp()
    {
        try
        {
            UdonSharpCompilerV1.CompileSync();
            AssetDatabase.SaveAssets();
            Debug.Log("[UdonSharpHeadlessCompile] Compile finished.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UdonSharpHeadlessCompile] Compile failed.\n{ex}");
            EditorApplication.Exit(1);
        }
    }
}
#endif
```

### 2. Locate the editor binary

`~/Unity/Hub/Editor/<version>/Editor/Unity`, where `<version>` comes from `ProjectSettings/ProjectVersion.txt`
(this project: `2022.3.22f1`).

### 3. Run the compile

```bash
~/Unity/Hub/Editor/2022.3.22f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath "$HOME/.local/share/VRChatCreatorCompanion/VRChatProjects/vrc-boxing" \
  -executeMethod UdonSharpHeadlessCompile.CompileUdonSharp \
  -logFile /tmp/udonsharp-compile.log
```

Adjust `-projectPath` for other projects. Use a generous timeout (first run includes asset import and can
take minutes; a warm recompile of this project compiles ~40 scripts in a few seconds).

### 4. Verify

- Process exit code: `0` = success, `1` = exception in the helper.
- Log must contain `[UdonSharp] Compile of N scripts finished` and `[UdonSharpHeadlessCompile] Compile finished.`
- Any `error` lines (excluding the harmless `Licensing::Module ... No ULF license found` update warnings) mean failure — read the log.
- Outputs rewritten in place: `Assets/SerializedUdonPrograms/*.asset` (compiled Udon bytecode,
  `serializedProgramCompressedBytes`) and the `UdonSharpProgramAsset` `.asset` next to each script
  (e.g. `Assets/Scripts/BoxGloves.asset`). Check file mtimes if unsure.

## Rules and gotchas

- `-executeMethod` runs after script compilation, on the main thread — which is exactly what the UdonSharp
  compiler requires. Never run `CompileSync()` on a background thread.
- `-nographics` is required on headless Linux; `-batchmode` alone may still try to open a display.
- `-quit` is only a safety net; the helper calls `EditorApplication.Exit()` itself so the process never hangs.
- On a fresh machine, batch mode may need one-time license activation — add `-username` / `-password` /
  `-serial` if it refuses to start. License *update* warnings in the log do not block compilation.
- If `.cs` files were added or changed outside the editor, their program `.asset` files are created on
  import by `Assets/Editor/UdonSharpProgramAssetAutoGenerator.cs`; run this headless compile afterwards.
- Alternative (weaker): plain `Unity -batchmode -nographics -quit -projectPath ...` without
  `-executeMethod` also triggers a compile via `UdonSharpSettings.autoCompileOnModify`, but only for
  *changed* scripts. Use `-executeMethod` for a guaranteed forced full recompile.
- Alternative API: `UdonSharpProgramAsset.CompileAllCsPrograms(forceCompile: true)` — same idea, callable
  from another `-executeMethod` entry point.
- Building the world headless (VRC build pipeline via `-executeMethod`) also compiles UdonSharp as a side
  effect, but is much heavier than a program-only recompile.
- Do not attempt to compile UdonSharp with `dotnet` directly — the editor dependencies make it impossible.
