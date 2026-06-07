# Repository Guidelines

## Project Structure & Module Organization

This repository is `mystia-steward-companion`, a BepInEx IL2CPP Mod plus a Tauri desktop companion window for Touhou Mystia's Izakaya. Do not use older project names as product names.

- `apps/companion/`: Tauri + React companion app.
- `apps/companion/src/`: React workbench, recommendation UI, TypeScript recommendation logic, and structured data.
- `apps/companion/src-tauri/`: Tauri desktop shell and single-instance window control.
- `mods/bepinex/`: BepInEx plugin, runtime reflection, local API, C# recommendation mirror, data sync, and packaging scripts.
- `mods/bepinex/Data/`: JSON copied from `apps/companion/src/data`.
- `docs/`: development conventions, runtime notes, mechanics knowledge, and GitHub Actions release notes.

## Naming Rules

Use `mystia-steward-companion` for the repository, product name, package slug, Tauri product name, release artifacts, install folder, and user-facing project references. C# identifiers may use `MystiaStewardCompanion`. Legacy names may appear only in explicit migration code or upstream provenance notices, such as old config or localStorage migration.

If paths change, update README files, `docs/development-conventions.md`, `docs/repo-memory.md`, workflow files, and build scripts in the same change.

## Build, Test, and Development Commands

- `pnpm install`: install frontend and Tauri dependencies.
- `pnpm lint`: run ESLint.
- `pnpm build`: run TypeScript build checks and Vite production build.
- `pnpm tauri:build`: build the desktop companion app.
- `dotnet build mods/bepinex/MystiaStewardCompanion.BepInEx.csproj -c Release`: build the Mod DLL.
- `powershell -ExecutionPolicy Bypass -File mods\bepinex\tools\build-release.ps1`: full local release package build. Run this only when explicitly requested.

For Linux validation without release packaging, use local binaries when needed, for example `node_modules/.bin/eslint`, `node_modules/.bin/tsc`, `node_modules/.bin/vite`, and `/huyu/environment/dotnet/dotnet`.

## Coding Style & Naming Conventions

Use strict TypeScript and avoid `any` unless there is no practical typed alternative. React code should use function components and hooks. Imports inside `apps/companion/src` should use the `@/` alias. C# code uses nullable reference types, explicit models, and runtime reflection guards for IL2CPP objects.

Keep user-facing docs and normal UI copy in Chinese unless a bilingual Mod setting or API field requires English. Do not hard-code game balancing values in UI components; update structured JSON data and typed recommendation logic.

## Testing Guidelines

Before committing code changes, run `pnpm lint`, `pnpm build`, and the relevant `dotnet build` command when C# changes. Do not run package/release builds unless the user explicitly asks. For recommendation changes, verify both TypeScript and C# mirror logic and update the relevant docs.

## GitHub Actions & Release Rules

`.github/workflows/ci.yml` is manual-only frontend CI. `.github/workflows/release.yml` runs only on `v*` tags or explicit manual dispatch. Do not create tags or trigger Release workflow unless the user asks for a version build.

The release workflow requires a self-hosted Windows runner labeled `self-hosted`, `Windows`, `mystia-steward-companion`, plus the repository variable `MYSTIA_REFERENCE_DIR` pointing to local BepInEx/Unity reference DLLs.

## Commit & Pull Request Guidelines

Use concise Conventional Commit-style messages, such as `fix(mod): migrate companion naming`. Keep commits scoped and imperative. Preserve unrelated user changes. PRs should include behavior summary, validation results, linked issues when applicable, and screenshots for visible UI changes.
