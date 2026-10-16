# Quickstart: Verifying the Structure

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md) | **Contract**: [contracts/structure-invariants.md](contracts/structure-invariants.md)

Runnable validation for this feature's outcomes. No setup beyond the repo and
the .NET SDK.

## 1. Build and test (SC-003)

```sh
dotnet build fsharp-game.fsproj        # expected: Build succeeded. 0 Warning(s) 0 Error(s)
dotnet test test/fsharp-game.Tests.fsproj
# expected: Passed! - Failed: 0, Passed: 666, Skipped: 0, Total: 666
```

The build also proves invariant I-2 (order soundness): F# compiles in list
order, so any reference to a later file is a compile error.

## 2. Dependency-graph scan (SC-001, invariants I-1/I-3)

From the repo root, run this scan — it parses the compile list, maps every
cross-file module reference, and prints order and cluster violations:

```sh
python3 - <<'EOF'
import re, os, collections
fsproj = open('fsharp-game.fsproj').read()
files = re.findall(r'<Compile Include="([^"]+\.fs)"', fsproj)
mod_of = {}
for f in files:
    m = re.search(r'(?m)^module\s+(\w+)', open(f).read())
    mod_of[f] = m.group(1) if m else os.path.basename(f)[:-3]
names = sorted(set(mod_of.values()), key=len, reverse=True)
pat = re.compile(r'(?<![.\w])(' + '|'.join(names) + r')\s*\.')
def strip(src):
    src = re.sub(r'\(\*.*?\*\)', ' ', src, flags=re.S)
    return re.sub(r'//[^\n]*', ' ', src)
by_mod = collections.defaultdict(list)
for f in files: by_mod[mod_of[f]].append(f)
edges = collections.defaultdict(set)
for f in files:
    for r in set(pat.findall(strip(open(f).read()))):
        for g in by_mod[r]:
            if g != f: edges[f].add(g)
order = {f: i for i, f in enumerate(files)}
def cluster(f): return f.split('/')[0] if '/' in f else '(root)'
layers = ['narrative','core','physics','render','audio','world','state','editor','screen','(root)']
corder = {c: i for i, c in enumerate(layers)}
up = [(f, g) for f in files for g in edges.get(f, ()) if corder[cluster(g)] > corder[cluster(f)]]
print("upward cluster edges:", up if up else "NONE (expected)")
dup = {m: fs for m, fs in by_mod.items() if len(fs) > 1}
print("duplicate global modules:", dup if dup else "NONE (expected)")
EOF
```

Expected output:

```text
upward cluster edges: NONE (expected)
duplicate global modules: NONE (expected)
```

(Note: `narrative/` files are `namespace Narrative`, so `History`/`Engine`
there are not globals and do not collide — the duplicate check covers only
global modules.)

## 3. Folder = layer spot check (FR-002, FR-003)

```sh
ls state/     # includes: Undo.fs ScenarioFork.fs CueEngine.fs — no world/ file compiles after state/
ls render/    # includes: DebugOverlay.fs
ls world/ world/assets/ world/render/
```

## 4. No stale references (FR-008)

```sh
grep -rnE 'world/CueEngine|world/ScenarioFork|state/History|screen/DebugOverlay' \
  docs/ --include='*.md'          # expected: no hits
grep -rnE 'world/(AssetLoad|Assets|ActorAsset|EnvironmentAsset|ActorRender|EnvironmentRender|EnvironmentBatch|DialogueRender|PromptRender)\.fs' \
  docs/ --include='*.md'          # expected: no hits
grep -rn 'Engine\.fs' docs/ | grep -v 'narrative/Engine\.fs' | grep -v 'CueEngine\.fs'
                                   # expected: no hits (CueEngine was not renamed)
```

`specs/` is excluded everywhere: past feature directories are historical
records and intentionally still reference the old paths.

## 5. Documentation check (I-6)

Open `docs/PROJECT_STRUCTURE.md` — its layout tree names every file from step
3's listings (including `core/Diagnostics.fs`, `core/Lru.fs`, `core/Maths.fs`,
`render/InstanceBuffer.fs`) and no retired ones (`ZoneRegistry.fs`,
`ZoneRegistryPanel.fs`).
