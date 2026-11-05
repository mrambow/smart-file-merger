# SmartFileMerger Standalone Installer & Self-Updater Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide zero-dependency standalone distribution (win-x64 self-contained), 1-click auto-setup to `%LocalAppData%` with desktop/start menu shortcuts, and seamless in-app self-updating via GitHub Releases.

**Architecture:** Clean Architecture additions to Domain interfaces (`IAppInstallationService`, `IUpdateDownloadService`), Infrastructure implementations using Windows Shell link creation and atomic in-use file renaming, Presentation Spectre UI prompts, and GitHub Actions release automation.

**Tech Stack:** .NET 10.0 (C# 14), Spectre.Console, GitHub Actions (`release.yml`), Windows Shell Scripts / COM for `.lnk` shortcuts, `HttpClient` streaming.

**Spec:** [docs/superpowers/specs/2026-11-05-installer-and-self-updater-design.md](file:///C:/Code/smart-file-merger/docs/superpowers/specs/2026-11-05-installer-and-self-updater-design.md)

## Global Constraints

- Target Framework: `net10.0`
- Language: C# 14, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- Strict Ponytail Principle: Maximum indentation depth 2, guard clauses at top of methods.
- Zero direct `System.IO` in Domain layer; all filesystem/OS operations in Infrastructure layer.
- Zero console calls in Domain or Infrastructure; all prompts via `IUserInterface`.
- All tests must pass cleanly (`dotnet test`) with 0 warnings under `--warnaserror`.

## Review Focus

1. **Installed Location Detection:** Running from outside `%LocalAppData%\Programs\SmartFileMerger` prompts for setup; running from inside bypasses prompt. (Tested in Task 3)
2. **Missing Shortcut Folders or Existing Shortcuts:** `WindowsAppInstallationService` must ensure desktop/start menu directories exist and overwrite stale `.lnk` files without throwing. (Tested in Task 3)
3. **Atomic File Swap of Running Executable:** In-use executable must be renamed to `.old` before `.new` replaces it, avoiding Windows sharing violations (`IOException`). (Tested in Task 4)
4. **Download Failure / Disconnection:** Network errors during update download must abort cleanly, remove partial `.new` file, and leave current executable untouched. (Tested in Task 4)
5. **Startup Cleanup of Stale `.old` Files:** Startup checks for `*.old` files and deletes them; if locked, safely catches exception without preventing startup. (Tested in Task 4)

---

## File Structure Map

```text
.github/
  workflows/
    release.yml                                     # NEW: Release build on git tags
src/SmartFileMerger/
  SmartFileMerger.csproj                            # UPDATED: publish profiles (win-x64, single-file)
  Program.cs                                        # UPDATED: startup cleanup, auto-setup, update prompt
  Domain/
    Interfaces/
      IAppInstallationService.cs                    # NEW
      IUpdateDownloadService.cs                     # NEW
      IUserInterface.cs                             # UPDATED: Setup & Update download prompts
  Infrastructure/
    WindowsAppInstallationService.cs                # NEW: LocalAppData copy & shortcut creation
    WindowsUpdateDownloadService.cs                 # NEW: Release download & atomic file swap
  Presentation/
    SpectreConsoleUserInterface.cs                  # UPDATED: Prompts & download progress bar
tests/SmartFileMerger.Tests/
  Infrastructure/
    WindowsAppInstallationServiceTests.cs           # NEW
    WindowsUpdateDownloadServiceTests.cs            # NEW
  Presentation/
    SpectreConsoleUserInterfaceTests.cs             # UPDATED
  EndToEndWorkflowTests.cs                          # UPDATED
```

---

### Task 1: GitHub Actions Release Workflow & Project Publish Profile

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `src/SmartFileMerger/SmartFileMerger.csproj`

**Interfaces:**
- Consumes: None
- Produces: Automated release build pipeline on tag pushes (`v*.*.*`) publishing self-contained `win-x64` binary.

- [ ] **Step 1: Configure Publish Properties in `SmartFileMerger.csproj`**
  - Add property group for `Release` configuration or publish defaults:
    - `<PublishSingleFile>true</PublishSingleFile>`
    - `<SelfContained>true</SelfContained>`
    - `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
    - `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`
    - `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>`

- [ ] **Step 2: Create `.github/workflows/release.yml`**
  - Trigger on `push: tags: ['v*']` and `workflow_dispatch`.
  - Runs on `windows-latest`.
  - Runs `dotnet test --configuration Release`.
  - Runs `dotnet publish src/SmartFileMerger/SmartFileMerger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./publish`.
  - Uploads `./publish/SmartFileMerger.exe` to GitHub Releases using `softprops/action-gh-release@v2`.

- [ ] **Step 3: Verify local test and compilation**
  - Run: `dotnet build --configuration Release --warnaserror`
  - Run: `dotnet test --configuration Release`
  - Expected: PASS with 0 warnings.

- [ ] **Step 4: Commit**
  ```bash
  git add .github/workflows/release.yml src/SmartFileMerger/SmartFileMerger.csproj
  git commit -m "ci: add GitHub Actions release workflow for self-contained win-x64 binary"
  ```

---

### Task 2: Domain Interfaces & Extended UI Contracts

**Files:**
- Create: `src/SmartFileMerger/Domain/Interfaces/IAppInstallationService.cs`
- Create: `src/SmartFileMerger/Domain/Interfaces/IUpdateDownloadService.cs`
- Modify: `src/SmartFileMerger/Domain/Interfaces/IUserInterface.cs`
- Modify: `tests/SmartFileMerger.Tests/Presentation/SpectreConsoleUserInterfaceTests.cs`
- Modify: `src/SmartFileMerger/Presentation/SpectreConsoleUserInterface.cs`

**Interfaces:**
- Consumes: None
- Produces: `IAppInstallationService`, `IUpdateDownloadService`, updated `IUserInterface`

- [ ] **Step 1: Write Domain Interfaces**
  - Create `IAppInstallationService.cs`:
    ```csharp
    namespace SmartFileMerger.Domain.Interfaces;

    public interface IAppInstallationService
    {
        bool IsRunningFromInstalledLocation();
        string GetInstalledDirectoryPath();
        string GetInstalledExecutablePath();
        void InstallToUserProgramsAndCreateShortcuts();
    }
    ```
  - Create `IUpdateDownloadService.cs`:
    ```csharp
    namespace SmartFileMerger.Domain.Interfaces;

    public interface IUpdateDownloadService
    {
        void CleanupOldExecutableIfExists();
        Task<bool> DownloadAndApplyUpdateAsync(
            string downloadUrl,
            Action<double> progressCallback,
            CancellationToken cancellationToken = default);
    }
    ```
  - Add to `IUserInterface.cs`:
    - `bool PromptForInstallation();`
    - `bool PromptForUpdateInstallation(string latestVersion);`
    - `void RunWithDownloadProgress(string version, Func<Action<double>, Task> downloadWork);`

- [ ] **Step 2: Implement UI Methods in `SpectreConsoleUserInterface.cs` & Unit Tests**
  - `PromptForInstallation`: Prompts `_console.Confirm("Möchtest du SmartFileMerger installieren (Desktop- und Startmenü-Verknüpfung anlegen)?", defaultValue: true)`.
  - `PromptForUpdateInstallation`: Prompts `_console.Confirm($"Ein neues Update ({Markup.Escape(latestVersion)}) ist verfügbar! Jetzt automatisch herunterladen und installieren?", defaultValue: true)`.
  - `RunWithDownloadProgress`: Shows progress bar updating with percentage.
  - Add unit tests in `SpectreConsoleUserInterfaceTests.cs` using `TestConsole`.

- [ ] **Step 3: Verify build and test suite**
  - Run: `dotnet build --warnaserror`
  - Run: `dotnet test`
  - Expected: PASS with 0 warnings.

- [ ] **Step 4: Commit**
  ```bash
  git add src/SmartFileMerger/Domain/Interfaces src/SmartFileMerger/Presentation tests/SmartFileMerger.Tests
  git commit -m "feat(domain): add IAppInstallationService, IUpdateDownloadService, and UI prompt contracts"
  ```

---

### Task 3: Windows App Installation Service & Shortcuts

**Files:**
- Create: `src/SmartFileMerger/Infrastructure/WindowsAppInstallationService.cs`
- Create: `tests/SmartFileMerger.Tests/Infrastructure/WindowsAppInstallationServiceTests.cs`

**Interfaces:**
- Consumes: `IAppInstallationService`, `IFileSystemService`
- Produces: `WindowsAppInstallationService` handling `%LocalAppData%` copy and `.lnk` shortcut creation.

- [ ] **Step 1: Write tests for `WindowsAppInstallationService`**
  - Test `GetInstalledDirectoryPath` returns `%LocalAppData%\Programs\SmartFileMerger`.
  - Test `IsRunningFromInstalledLocation` returns true when current process path equals installed path (case-insensitive).
  - Test `InstallToUserProgramsAndCreateShortcuts` copies file and generates desktop & start menu shortcuts in designated test directory.

- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~WindowsAppInstallationServiceTests"`
  - Expected: FAIL.

- [ ] **Step 3: Implement `WindowsAppInstallationService`**
  - Clean implementation following Ponytail principle:
    - Path calculation: `%LocalAppData%\Programs\SmartFileMerger\SmartFileMerger.exe`.
    - `IsRunningFromInstalledLocation()` compares `Environment.ProcessPath`.
    - `InstallToUserProgramsAndCreateShortcuts()`:
      - Ensures installed directory exists.
      - Copies current process binary to target location.
      - Creates `.lnk` shortcuts using PowerShell/WScript Shell COM helper or shell link utility for:
        - `Environment.GetFolderPath(SpecialFolder.Desktop)`
        - `Environment.GetFolderPath(SpecialFolder.StartMenu)\Programs`

- [ ] **Step 4: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~WindowsAppInstallationServiceTests"`
  - Expected: PASS.

- [ ] **Step 5: Commit**
  ```bash
  git add src/SmartFileMerger/Infrastructure/WindowsAppInstallationService.cs tests/SmartFileMerger.Tests/Infrastructure/WindowsAppInstallationServiceTests.cs
  git commit -m "feat(infrastructure): implement WindowsAppInstallationService with desktop and start menu shortcuts"
  ```

---

### Task 4: Windows Update Download Service & In-Use Swap

**Files:**
- Create: `src/SmartFileMerger/Infrastructure/WindowsUpdateDownloadService.cs`
- Create: `tests/SmartFileMerger.Tests/Infrastructure/WindowsUpdateDownloadServiceTests.cs`

**Interfaces:**
- Consumes: `IUpdateDownloadService`, `IFileSystemService`
- Produces: `WindowsUpdateDownloadService` handling stream download and Windows atomic `.old`/`.new` swap.

- [ ] **Step 1: Write tests for `WindowsUpdateDownloadService`**
  - Test `CleanupOldExecutableIfExists`: deletes `SmartFileMerger.exe.old` if present; handles locked file gracefully without throwing.
  - Test `DownloadAndApplyUpdateAsync`:
    - Simulates download to `*.new`.
    - Simulates file swap: current file renamed to `*.old`, `*.new` moved to current path.
    - Simulates download error: leaves original file untouched, removes partial `*.new` file.

- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~WindowsUpdateDownloadServiceTests"`
  - Expected: FAIL.

- [ ] **Step 3: Implement `WindowsUpdateDownloadService`**
  - `CleanupOldExecutableIfExists()`:
    - Checks if `{currentExePath}.old` exists.
    - Strips ReadOnly, attempts `File.Delete`. Catches `IOException` silently (if still locked by decaying process).
  - `DownloadAndApplyUpdateAsync(...)`:
    - Streams HTTP response content with progress calculation (`bytesRead / totalBytes`).
    - Writes to `{currentExePath}.new`.
    - Atomically renames:
      1. `File.Move(currentExePath, currentExePath + ".old", overwrite: true)`
      2. `File.Move(currentExePath + ".new", currentExePath, overwrite: false)`
    - On failure: cleans up `*.new` if it exists, restores `.old` if necessary, returns `false`.

- [ ] **Step 4: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~WindowsUpdateDownloadServiceTests"`
  - Expected: PASS.

- [ ] **Step 5: Commit**
  ```bash
  git add src/SmartFileMerger/Infrastructure/WindowsUpdateDownloadService.cs tests/SmartFileMerger.Tests/Infrastructure/WindowsUpdateDownloadServiceTests.cs
  git commit -m "feat(infrastructure): implement WindowsUpdateDownloadService with in-use atomic swap"
  ```

---

### Task 5: Program.cs Startup Flow Integration & End-to-End Verification

**Files:**
- Modify: `src/SmartFileMerger/Program.cs`
- Modify: `tests/SmartFileMerger.Tests/EndToEndWorkflowTests.cs`

**Interfaces:**
- Consumes: All services
- Produces: Fully integrated application with startup cleanup, auto-setup, and self-update prompt.

- [ ] **Step 1: Wire Services & Startup Sequence in `Program.cs`**
  - Register `IAppInstallationService` -> `WindowsAppInstallationService`.
  - Register `IUpdateDownloadService` -> `WindowsUpdateDownloadService`.
  - In `Program.RunAsync`:
    1. Call `updateDownloadService.CleanupOldExecutableIfExists()`.
    2. Check `appInstallationService.IsRunningFromInstalledLocation()`:
       - If false, call `ui.PromptForInstallation()`. If true, call `appInstallationService.InstallToUserProgramsAndCreateShortcuts()`, launch installed executable, and return 0.
    3. Check `updateChecker.CheckForUpdateAsync(...)`:
       - If update available, call `ui.PromptForUpdateInstallation(latestVersion)`.
       - If confirmed, execute `updateDownloadService.DownloadAndApplyUpdateAsync(...)`. If successful, launch updated executable and return 0.
    4. Proceed with standard merge workflow if no update or update declined.

- [ ] **Step 2: Add End-to-End Verification Tests**
  - Test startup cleanup of old executable.
  - Test auto-setup trigger when running outside installed directory.
  - Test update trigger and prompt delegation.

- [ ] **Step 3: Run full verification suite**
  - Run: `dotnet build --warnaserror`
  - Run: `dotnet test`
  - Expected: 100% PASS, 0 failures, 0 warnings.

- [ ] **Step 4: Commit**
  ```bash
  git add src/SmartFileMerger/Program.cs tests/SmartFileMerger.Tests/EndToEndWorkflowTests.cs
  git commit -m "feat: integrate auto-setup and self-update into application startup sequence"
  ```
