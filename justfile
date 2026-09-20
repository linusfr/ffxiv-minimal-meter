#!/usr/bin/env just --justfile
# ==========================================
# DEFAULT
# ==========================================

# Show all available commands
@default:
    just --list

# ==========================================
# BUILD
# ==========================================

project := "src/MinimalMeter.csproj"
dev_plugins := env_var('HOME') / ".xlcore/devPlugins/MinimalMeter"
# Dalamud's SDK needs the game's assemblies; XIVLauncher.Core already has them.
dalamud_home := env_var('HOME') / ".xlcore/dalamud/Hooks/dev"
# NixOS has no system dotnet — pull the SDK from nixpkgs for the duration.
dotnet := "nix shell nixpkgs#dotnet-sdk_10 -c env DALAMUD_HOME=" + dalamud_home + " DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet"

# Restore NuGet packages
restore:
    {{dotnet}} restore {{project}}

# Debug build
build: restore
    {{dotnet}} build {{project}} --configuration Debug --no-restore

# Release build (what CI ships)
build-release: restore
    {{dotnet}} build {{project}} --configuration Release --no-restore

# Build and drop the plugin into XIVLauncher's devPlugins for /xlplugins dev mode
install: build
    mkdir -p {{dev_plugins}}
    cp src/bin/Debug/MinimalMeter.dll {{dev_plugins}}/
    cp src/bin/Debug/SkiaSharp.dll {{dev_plugins}}/ 2>/dev/null || true
    @echo "Installed to {{dev_plugins}} — reload dev plugins in /xlplugins"

# Remove build output
clean:
    rm -rf src/bin src/obj MinimalMeter.zip pack/

# ==========================================
# QUALITY
# ==========================================

# Format C# in place.
# Deliberately skips the `whitespace` pass: this codebase aligns fields and
# comments into columns, and that pass would flatten all of it.
fmt:
    {{dotnet}} format style {{project}}
    {{dotnet}} format analyzers {{project}}

# Verify formatting without writing (same passes as `fmt`)
fmt-check:
    {{dotnet}} format style {{project}} --verify-no-changes
    {{dotnet}} format analyzers {{project}} --verify-no-changes

# Run every pre-commit hook over the whole tree
check:
    prek run --all-files

# Install the git hooks
hooks:
    prek install --install-hooks
    prek install --hook-type commit-msg

# ==========================================
# RELEASE
# ==========================================

# Version that CI would cut next, without cutting it
version-next:
    @docker run --rm -v $PWD:/repo -w /repo ghcr.io/go-semantic-release/semantic-release:latest --dry --no-ci 2>/dev/null || echo "needs docker; CI computes this on push to main"

# Version currently declared in the csproj
@version:
    grep -oP '(?<=<Version Condition="\$\(Version\)... == ..>)[^<]+' {{project}} || grep -m1 -oP '(?<=<Version[^>]*>)[^<]+' {{project}}
