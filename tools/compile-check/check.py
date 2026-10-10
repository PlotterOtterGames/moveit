"""Compile-checks the Unity project's C# without Unity (cloud sessions, CI).

For every assembly definition under unity/MoveItGames/Assets it writes a .NET project with the same sources, defines
and references (other asmdefs become project references), compiled as C# 9 against Unity's UnityEngine reference
assemblies (NuGet package UnityEngine.Modules, 2021.3: Unity 6 only adds to it) plus small stand-ins for packages
(Stubs/: the Input System, the test runner). Then builds them twice: as the Android player and as the editor.

Catches what blind edits break: typos, wrong types or members, a missing asmdef reference, internal members used from
another assembly, newer C# than Unity's. Can't check: the MoveIt.Editor assembly (needs UnityEditor), shaders, scenes.
APIs that Unity 6 added after 2021.3 are unknown to the reference assemblies: errors about the names in UNITY6_ONLY are
ignored (add a name there when the game starts using another one, after checking Unity 6's documentation).

    python3 tools/compile-check/check.py            # both configurations
    python3 tools/compile-check/check.py --player   # just one
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
ASSETS = ROOT / "unity" / "MoveItGames" / "Assets"
OUT = HERE / "generated"
UNITY_MODULES = ("UnityEngine.Modules", "2021.3.33")
NUNIT = ("NUnit", "3.14.0")
# package assemblies -> the stand-in that replaces them (None = nothing the code uses)
STUBS = {
    "Unity.InputSystem": "Stubs/InputSystem.cs",
    "UnityEngine.TestRunner": "Stubs/TestRunner.cs",
    "UnityEditor.TestRunner": None,
}
UNITY6_ONLY = {"PhysicsMaterial", "PhysicsMaterialCombine", "linearVelocity", "linearDamping", "angularDamping"}
CONFIGS = {
    "player": "UNITY_ANDROID;DEVELOPMENT_BUILD",
    "editor": "UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_STANDALONE_WIN;UNITY_STANDALONE",
}
COMMON_DEFINES = "UNITY_2021_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_6000_3_OR_NEWER;ENABLE_INPUT_SYSTEM"
PROPS = f"""  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <NoWarn>$(NoWarn);CS0649;CS0169;CS0414;CS0618;CS0067;CS1998;CS8632</NoWarn>
    <DefineConstants>$(DefineConstants);{COMMON_DEFINES};$(UnityDefines)</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="{UNITY_MODULES[0]}" Version="{UNITY_MODULES[1]}" />
  </ItemGroup>
"""


def asmdefs():
    found = []
    for path in sorted(ASSETS.rglob("*.asmdef")):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        found.append((path.parent, data))
    return found


def sources(folder, all_folders):
    """The .cs files of an asmdef: its folder and below, except folders with their own asmdef."""
    nested = [f for f in all_folders if f != folder and folder in f.parents]
    return [p for p in sorted(folder.rglob("*.cs")) if not any(n in p.parents for n in nested)]


def needs_editor(files):
    pattern = re.compile(r"^\s*using\s+UnityEditor(\.|;)|\bUnityEditor\.(?!TestRunner)", re.M)
    return any(pattern.search(f.read_text(encoding="utf-8-sig")) for f in files)


def project(name, files, defines=(), refs=(), packages=(), unsafe=False):
    items = "\n".join(f'    <Compile Include="{f}" />' for f in files)
    refs_xml = "\n".join(f'    <ProjectReference Include="../{r}/{r}.csproj" />' for r in refs)
    pkgs = "\n".join(f'    <PackageReference Include="{p}" Version="{v}" />' for p, v in packages)
    extra = f"<DefineConstants>$(DefineConstants);{';'.join(defines)}</DefineConstants>" if defines else ""
    text = f"""<Project Sdk="Microsoft.NET.Sdk">
{PROPS}  <PropertyGroup>
    <AssemblyName>{name}</AssemblyName>
    <AllowUnsafeBlocks>{str(unsafe).lower()}</AllowUnsafeBlocks>
    {extra}
  </PropertyGroup>
  <ItemGroup>
{items}
  </ItemGroup>
  <ItemGroup>
{refs_xml}
{pkgs}
  </ItemGroup>
</Project>
"""
    folder = OUT / name
    folder.mkdir(parents=True, exist_ok=True)
    (folder / f"{name}.csproj").write_text(text)


def generate():
    defs = asmdefs()
    folders = [f for f, _ in defs]
    names = []
    skipped = []
    for stub, file in STUBS.items():
        if file:
            project(stub, [HERE / file])
    for folder, data in defs:
        name = data["name"]
        files = sources(folder, folders)
        if not files:
            continue
        if needs_editor(files):
            skipped.append(name)
            continue
        refs, packages = [], []
        for r in data.get("references", []):
            if r.startswith("GUID:"):
                sys.exit(f"{name}: GUID references aren't supported, use names")
            if r in STUBS:
                if STUBS[r]:
                    refs.append(r)
            elif r.startswith("Unity."):
                print(f"note: {name} references the package {r}: add a stand-in to Stubs/ if its code is used")
            else:
                refs.append(r)
        if "nunit.framework.dll" in data.get("precompiledReferences", []):
            packages.append(NUNIT)
        project(name, files, data.get("defineConstraints", []), refs, packages, data.get("allowUnsafeCode", False))
        names.append(name)
    proj = "\n".join(f'    <ProjectReference Include="{n}/{n}.csproj" />' for n in names)
    (OUT / "All.proj").write_text(f"""<Project Sdk="Microsoft.Build.Traversal/4.1.0">
  <ItemGroup>
{proj}
  </ItemGroup>
</Project>
""")
    return names, skipped


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--player", action="store_true", help="only the Android player configuration")
    parser.add_argument("--editor", action="store_true", help="only the editor configuration")
    args = parser.parse_args()
    names, skipped = generate()
    configs = [c for c in CONFIGS if getattr(args, c)] or list(CONFIGS)
    print(f"{len(names)} assemblies" + (f" (not checked, they need UnityEditor: {', '.join(skipped)})" if skipped else ""))
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    failed = False
    for config in configs:
        result = subprocess.run(
            ["dotnet", "build", str(OUT / "All.proj"), "-nologo", "-v", "q", "-clp:NoSummary;ErrorsOnly",
             f"-p:UnityDefines={CONFIGS[config].replace(';', '%3B')}", f"-p:BaseIntermediateOutputPath=obj/{config}/",
             f"-p:OutputPath=bin/{config}/"],
            cwd=OUT, env=env, capture_output=True, text=True)
        errors = sorted({line.strip() for line in (result.stdout + result.stderr).splitlines() if ": error " in line})
        unity6 = [e for e in errors if any(f"'{n}'" in e for n in UNITY6_ONLY)]
        errors = [e for e in errors if e not in unity6]
        note = f" ({len(unity6)} about Unity 6 APIs ignored)" if unity6 else ""
        if not errors and (result.returncode == 0 or unity6):
            print(f"{config}: OK{note}")
            continue
        failed = True
        print(f"{config}: {len(errors)} error(s){note}")
        for e in errors[:60]:
            print("  " + re.sub(r" \[[^\]]*\.csproj\]$", "", e.replace(str(ROOT) + "/", "")))
        if len(errors) > 60:
            print(f"  ... and {len(errors) - 60} more")
        if not errors:
            print(result.stdout[-3000:], result.stderr[-3000:])
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
