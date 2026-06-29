# Building

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- The project targets `net10.0` with Blazor Interactive Server components.

## Quick Build

Build for all platforms in one go (output goes to `dist/<rid>/`):

```bash
# Build all
./build-all.sh
```

Or build individually:

```bash
dotnet publish -c Release -r win-x64     --self-contained true -p:PublishSingleFile=true -o dist/win-x64
dotnet publish -c Release -r osx-x64     --self-contained true -p:PublishSingleFile=true -o dist/osx-x64
dotnet publish -c Release -r osx-arm64   --self-contained true -p:PublishSingleFile=true -o dist/osx-arm64
dotnet publish -c Release -r linux-x64   --self-contained true -p:PublishSingleFile=true -o dist/linux-x64
dotnet publish -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -o dist/linux-arm64
```

Each command produces a single-file self-contained binary with all dependencies included — no .NET runtime required on the target machine.

## Build Flags Explained

| Flag | Purpose |
|------|---------|
| `-c Release` | Optimised release build |
| `-r <rid>` | Target runtime identifier (see table below) |
| `--self-contained true` | Bundles the .NET runtime so the target machine doesn't need it |
| `-p:PublishSingleFile=true` | Packs everything into a single executable |
| `-o dist/<rid>` | Output directory |

> **Note:** Use an output path **outside** the project tree (like `dist/`) to avoid Blazor static web asset scanning conflicts when building for multiple RIDs. See [Caveats](#caveats).

## Supported RIDs

| RID | Platform | Arch |
|-----|----------|------|
| `win-x64` | Windows | x86-64 |
| `osx-x64` | macOS | x86-64† |
| `osx-arm64` | macOS | Apple Silicon |
| `linux-x64` | Linux | x86-64 |
| `linux-arm64` | Linux | ARM64 (e.g., Raspberry Pi, OrbStack) |

† For Apple Silicon Macs, prefer `osx-arm64` for better performance. The `osx-x64` binary runs under Rosetta 2.

## Output Layout

```
dist/<rid>/
├── RedditShortMaker              # or RedditShortMaker.exe on Windows
├── RedditShortMaker.pdb          # debug symbols
├── RedditShortMaker.deps.json
├── RedditShortMaker.runtimeconfig.json
├── RedditShortMaker.staticwebassets.endpoints.json
├── appsettings.json
├── appsettings.Development.json
├── wwwroot/                # static assets
└── outputs/                # runtime output directory (created automatically)
```

## Running

```bash
# Default (http://localhost:5000)
./dist/linux-x64/RedditShortMaker

# Custom port / bind address
./dist/linux-x64/RedditShortMaker --urls "http://0.0.0.0:8080"
```

## Caveats

- **Build output location matters.** When publishing for multiple RIDs, use separate output directories **outside** the project folder (e.g., `dist/<rid>/`). If you publish inside the project tree (like `publish/`), the Blazor build system may scan previous RID outputs and fail with `BLAZOR106`.
- **Clean between RIDs** if reusing the same output directory — `bin/` and `obj/` cache per-RID builds correctly, but publish outputs should be isolated.
- The project uses SkiaSharp which has native platform dependencies (like `libSkiaSharp.dylib` / `libSkiaSharp.dll`). Because we publish to a single-file executable, we configure `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` in `RedditShortMaker.csproj` to bundle these native libraries inside the binary, allowing them to extract and load automatically at runtime.
- **Rosetta 2 vs Native:** If you run the `osx-x64` binary on an Apple Silicon (M1/M2/M3) Mac, it will run via Rosetta translation. For optimal performance and native compatibility, use the `osx-arm64` binary build instead.
