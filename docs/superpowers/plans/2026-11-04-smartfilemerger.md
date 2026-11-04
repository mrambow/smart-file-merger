# SmartFileMerger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a robust, interactive Windows console application in .NET 10 to safely merge directories with non-blocking conflict collection, automated duplicate detection, and interactive conflict resolution via Spectre.Console.

**Architecture:** Pragmatic Clean Architecture / Domain-Driven Design in a single-project solution with a dedicated xUnit test project. Strict separation between immutable Domain models, Application orchestration/strategies, Infrastructure Windows filesystem access, and Presentation Spectre.Console UI.

**Tech Stack:** .NET 10.0 (C# 14), Spectre.Console 0.49+, Microsoft.Extensions.DependencyInjection, xUnit, FluentAssertions, NSubstitute.

**Spec:** [docs/superpowers/specs/2026-11-04-smartfilemerger-design.md](file:///C:/Code/smart-file-merger/docs/superpowers/specs/2026-11-04-smartfilemerger-design.md)

## Global Constraints

- Target Framework: `net10.0`
- Language: C# 14, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- Ponytail-Principle: Flat hierarchy, max indentation depth 2, early returns / guard clauses at method top.
- No direct `System.IO` calls in Domain or Application layers; all filesystem operations must pass through `IFileSystemService`.
- No interactive console calls in Domain, Application, or Infrastructure layers; all UI interactions must pass through `IUserInterface`.
- Windows filesystem compatibility: Handle `FileAttributes.ReadOnly`, preserve timestamps across volumes, handle `IOException` sharing violations without aborting.

## Review Focus

1. **Target is subdirectory of Source:** Calling `DirectoryRoot.IsSubdirectoryOf` or attempting to merge `C:\Data` into `C:\Data\Sub` must be rejected immediately to avoid infinite recursion. (Tested in Task 2)
2. **Windows Drag-and-Drop Path Quotes:** Paths entered with wrapping double or single quotes (`"C:\My Folder"`) must be stripped and normalized without throwing invalid character errors. (Tested in Task 2)
3. **ReadOnly Target on Overwrite:** Overwriting a read-only target file must strip `FileAttributes.ReadOnly` before calling `File.Move(..., overwrite: true)` to avoid `UnauthorizedAccessException`. (Tested in Task 5)
4. **Active File Lock (Sharing Violation):** When a source file is open by another process, `MergeOrchestrator` must catch `IOException`, log the failure, increment `ErrorsOccurred`, and continue with next file. (Tested in Task 4)
5. **Cross-Volume Timestamp Loss:** Moving files across drive letters must explicitly preserve and restore `CreationTimeUtc` and `LastWriteTimeUtc` on the target file. (Tested in Task 5)

---

## File Structure Map

```text
src/SmartFileMerger/
  SmartFileMerger.csproj
  Program.cs
  Domain/
    Models/
      DirectoryRoot.cs
      RelativeFilePath.cs
      FileMetadata.cs
      FileConflict.cs
      MergeSummary.cs
    Interfaces/
      IFileSystemService.cs
      IConflictResolutionStrategy.cs
      IUserInterface.cs
      IUpdateCheckService.cs
  Application/
    MergeOrchestrator.cs
    ConflictResolutionRunner.cs
    Strategies/
      OverwriteTargetStrategy.cs
      MoveOrRenameTargetStrategy.cs
      RenameSourceStrategy.cs
      SkipConflictStrategy.cs
  Infrastructure/
    WindowsFileSystemService.cs
    GitHubUpdateCheckService.cs
  Presentation/
    SpectreConsoleUserInterface.cs

tests/SmartFileMerger.Tests/
  SmartFileMerger.Tests.csproj
  Domain/
    DirectoryRootTests.cs
    RelativeFilePathTests.cs
    FileConflictTests.cs
  Application/
    MergeOrchestratorTests.cs
    StrategyTests.cs
  Infrastructure/
    WindowsFileSystemServiceIntegrationTests.cs

.github/
  workflows/
    ci.yml
    claude-pr-gate.yml
.editorconfig
AGENTS.md
LICENSE
README.md
```

---

### Task 1: Repository Setup, Governance & Solution Scaffolding

**Files:**
- Create: `.editorconfig`
- Create: `LICENSE`
- Create: `README.md`
- Create: `AGENTS.md`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/claude-pr-gate.yml`
- Create: `SmartFileMerger.sln`
- Create: `src/SmartFileMerger/SmartFileMerger.csproj`
- Create: `tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj`

**Interfaces:**
- Consumes: None
- Produces: Compilable .NET 10 solution with test project and CI governance.

- [ ] **Step 1: Create repository governance files**
  - Create `.editorconfig` with standard C# rules (4 spaces, UTF-8, LF/CRLF normalization).
  - Create `LICENSE` (MIT, Marc Rambow).
  - Create `README.md` with features, architecture summary, installation, and usage guide.
  - Create `AGENTS.md` documenting DDD record typing, Ponytail principle, and no-IO in application rules.

- [ ] **Step 2: Create GitHub Action Workflows**
  - Create `.github/workflows/ci.yml` running `dotnet build` and `dotnet test` on PR and push to `main`.
  - Create `.github/workflows/claude-pr-gate.yml` with `pull_request_target` event on `types: [labeled]`, strictly gated to `github.event.label.name == 'review'` and sender `mrambow`.

- [ ] **Step 3: Scaffold Solution and Projects**
  - Run:
    ```bash
    dotnet new sln -n SmartFileMerger
    dotnet new console -n SmartFileMerger -o src/SmartFileMerger -f net10.0
    dotnet new xunit -n SmartFileMerger.Tests -o tests/SmartFileMerger.Tests -f net10.0
    dotnet sln SmartFileMerger.sln add src/SmartFileMerger/SmartFileMerger.csproj
    dotnet sln SmartFileMerger.sln add tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj
    dotnet add tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj reference src/SmartFileMerger/SmartFileMerger.csproj
    ```

- [ ] **Step 4: Add NuGet Dependencies**
  - In `src/SmartFileMerger`:
    ```bash
    dotnet add src/SmartFileMerger/SmartFileMerger.csproj package Spectre.Console --version 0.49.1
    dotnet add src/SmartFileMerger/SmartFileMerger.csproj package Microsoft.Extensions.DependencyInjection --version 10.0.0-preview.* || dotnet add src/SmartFileMerger/SmartFileMerger.csproj package Microsoft.Extensions.DependencyInjection
    ```
  - In `tests/SmartFileMerger.Tests`:
    ```bash
    dotnet add tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj package FluentAssertions
    dotnet add tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj package NSubstitute
    ```

- [ ] **Step 5: Verify build & tests pass**
  - Run: `dotnet build`
  - Run: `dotnet test`
  - Expected: Build succeeded, 1 default test passed.

- [ ] **Step 6: Commit**
  ```bash
  git add .
  git commit -m "chore: scaffold .NET 10 solution, governance docs, and CI workflows"
  ```

---

### Task 2: Domain Layer Models & Value Objects

**Files:**
- Create: `src/SmartFileMerger/Domain/Models/DirectoryRoot.cs`
- Create: `src/SmartFileMerger/Domain/Models/RelativeFilePath.cs`
- Create: `src/SmartFileMerger/Domain/Models/FileMetadata.cs`
- Create: `src/SmartFileMerger/Domain/Models/FileConflict.cs`
- Create: `src/SmartFileMerger/Domain/Models/MergeSummary.cs`
- Create: `tests/SmartFileMerger.Tests/Domain/DirectoryRootTests.cs`
- Create: `tests/SmartFileMerger.Tests/Domain/RelativeFilePathTests.cs`
- Create: `tests/SmartFileMerger.Tests/Domain/FileConflictTests.cs`

**Interfaces:**
- Consumes: None
- Produces: `DirectoryRoot`, `RelativeFilePath`, `FileMetadata`, `FileConflict`, `MergeSummary`

- [ ] **Step 1: Write failing tests for Domain Models**
  - Create `DirectoryRootTests`: test null/empty rejection, quote stripping, combine with relative path, and `IsSubdirectoryOf` detection.
  - Create `RelativeFilePathTests`: test rooted path rejection, slash normalization, and property extraction (`FileName`, `Extension`).
  - Create `FileConflictTests`: test `SizeDifferenceInBytes`, `AgeDifference`, and `IsIdenticalSizeAndTimestamp` logic.

- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Domain"`
  - Expected: FAIL (Types not yet implemented).

- [ ] **Step 3: Implement Domain Models**
  - Implement `DirectoryRoot.cs`:
    ```csharp
    namespace SmartFileMerger.Domain.Models;

    public sealed record DirectoryRoot
    {
        public string FullPath { get; }

        public DirectoryRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Pfad darf nicht leer sein.", nameof(path));

            var cleaned = path.Trim().Trim('"', '\'');
            FullPath = Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public string CombineWith(RelativeFilePath relativePath) =>
            Path.Combine(FullPath, relativePath.Value);

        public bool IsSubdirectoryOf(DirectoryRoot potentialParent)
        {
            var parentPath = potentialParent.FullPath + Path.DirectorySeparatorChar;
            var thisPath = FullPath + Path.DirectorySeparatorChar;
            return thisPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
        }
    }
    ```
  - Implement `RelativeFilePath.cs`:
    ```csharp
    namespace SmartFileMerger.Domain.Models;

    public sealed record RelativeFilePath
    {
        public string Value { get; }

        public RelativeFilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Relativer Pfad darf nicht leer sein.", nameof(relativePath));

            var cleaned = relativePath.Trim().Trim('"', '\'');
            if (Path.IsPathRooted(cleaned))
                throw new ArgumentException("Pfad muss relativ sein.", nameof(relativePath));

            Value = cleaned.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        public string FileName => Path.GetFileName(Value);
        public string Extension => Path.GetExtension(Value);
        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value);
        public string? DirectoryName => Path.GetDirectoryName(Value);
    }
    ```
  - Implement `FileMetadata.cs`, `FileConflict.cs`, `MergeSummary.cs` as specified in spec.

- [ ] **Step 4: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Domain"`
  - Expected: PASS all domain tests.

- [ ] **Step 5: Commit**
  ```bash
  git add src/SmartFileMerger/Domain/Models tests/SmartFileMerger.Tests/Domain
  git commit -m "feat(domain): implement value objects DirectoryRoot, RelativeFilePath, FileConflict, and MergeSummary"
  ```

---

### Task 3: Domain Interfaces & Resolution Strategies

**Files:**
- Create: `src/SmartFileMerger/Domain/Interfaces/IFileSystemService.cs`
- Create: `src/SmartFileMerger/Domain/Interfaces/IConflictResolutionStrategy.cs`
- Create: `src/SmartFileMerger/Domain/Interfaces/IUserInterface.cs`
- Create: `src/SmartFileMerger/Domain/Interfaces/IUpdateCheckService.cs`
- Create: `src/SmartFileMerger/Application/Strategies/OverwriteTargetStrategy.cs`
- Create: `src/SmartFileMerger/Application/Strategies/MoveOrRenameTargetStrategy.cs`
- Create: `src/SmartFileMerger/Application/Strategies/RenameSourceStrategy.cs`
- Create: `src/SmartFileMerger/Application/Strategies/SkipConflictStrategy.cs`
- Create: `tests/SmartFileMerger.Tests/Application/StrategyTests.cs`

**Interfaces:**
- Consumes: `Domain.Models`
- Produces: `IFileSystemService`, `IConflictResolutionStrategy`, `IUserInterface`, `IUpdateCheckService`, 4 concrete strategies.

- [ ] **Step 1: Define Domain Interfaces**
  - Write `IFileSystemService.cs`, `IConflictResolutionStrategy.cs`, `IUserInterface.cs`, `IUpdateCheckService.cs` in `Domain/Interfaces`.

- [ ] **Step 2: Write failing tests for Strategies**
  - In `StrategyTests.cs`:
    - Test `OverwriteTargetStrategy`: calls `_fileSystem.MoveFile(src, dst, overwrite: true)`.
    - Test `MoveOrRenameTargetStrategy`: calls `PromptForCustomTargetFilePath`, moves target to backup, moves source to target.
    - Test `RenameSourceStrategy`: calls `PromptForNewFileName`, moves source to new target filename.
    - Test `SkipConflictStrategy`: does not invoke `MoveFile`.

- [ ] **Step 3: Run tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Application.StrategyTests"`
  - Expected: FAIL (Strategies not found).

- [ ] **Step 4: Implement Strategies**
  - Implement `OverwriteTargetStrategy`:
    ```csharp
    namespace SmartFileMerger.Application.Strategies;

    public sealed class OverwriteTargetStrategy(IFileSystemService fileSystem) : IConflictResolutionStrategy
    {
        public string Key => "overwrite";
        public string DisplayName => "Ziel in B überschreiben (Quelle A gewinnt)";

        public void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot)
        {
            fileSystem.MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: true);
        }
    }
    ```
  - Implement `MoveOrRenameTargetStrategy`, `RenameSourceStrategy`, and `SkipConflictStrategy` using constructor-injected `IFileSystemService` and `IUserInterface`.

- [ ] **Step 5: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Application.StrategyTests"`
  - Expected: PASS.

- [ ] **Step 6: Commit**
  ```bash
  git add src/SmartFileMerger/Domain/Interfaces src/SmartFileMerger/Application/Strategies tests/SmartFileMerger.Tests/Application/StrategyTests.cs
  git commit -m "feat(application): implement conflict resolution strategies"
  ```

---

### Task 4: Application Layer: MergeOrchestrator & ConflictResolutionRunner

**Files:**
- Create: `src/SmartFileMerger/Application/MergeOrchestrator.cs`
- Create: `src/SmartFileMerger/Application/ConflictResolutionRunner.cs`
- Create: `tests/SmartFileMerger.Tests/Application/MergeOrchestratorTests.cs`

**Interfaces:**
- Consumes: `IFileSystemService`, `IUserInterface`, `IConflictResolutionStrategy`, `Domain.Models`
- Produces: `MergeOrchestrator.ExecuteAsync`, `ConflictResolutionRunner.ResolveConflicts`

- [ ] **Step 1: Write failing tests for `MergeOrchestrator`**
  - Test validation guards (Source directory does not exist, Target is same as Source, Target is subdirectory of Source).
  - Test direct move for files without conflict (`FilesMovedDirectly == 1`).
  - Test automatic skip & source deletion for identical files (`conflict.IsIdenticalSizeAndTimestamp` -> `IdenticalFilesSkipped == 1`, `DeleteFile` called).
  - Test conflict queuing for non-identical existing target files (`ConflictsDetected == 1`).
  - Test fault tolerance: `IOException` during move increments `ErrorsOccurred` and does not throw.

- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Application.MergeOrchestratorTests"`
  - Expected: FAIL.

- [ ] **Step 3: Implement `MergeOrchestrator` and `ConflictResolutionRunner`**
  - `MergeOrchestrator` implementation following the Ponytail principle:
    - Guard clauses at the top (`ValidateDirectories`).
    - File enumeration via `_fileSystem.GetAllRelativeFilePaths(source)`.
    - Partition files into direct move, identical skip, or conflict queue.
    - Safe exception handling per file catching `IOException` and `UnauthorizedAccessException`.
    - Post-conflict cleanup call `_fileSystem.RemoveEmptyDirectories(source)`.
  - `ConflictResolutionRunner` implementation:
    - Iterates conflicts, calls `_ui.PromptForConflictResolution`, executes chosen strategy, counts resolved vs skipped conflicts.

- [ ] **Step 4: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Application.MergeOrchestratorTests"`
  - Expected: PASS all orchestrator tests.

- [ ] **Step 5: Commit**
  ```bash
  git add src/SmartFileMerger/Application tests/SmartFileMerger.Tests/Application/MergeOrchestratorTests.cs
  git commit -m "feat(application): implement MergeOrchestrator and ConflictResolutionRunner"
  ```

---

### Task 5: Infrastructure Layer: WindowsFileSystemService & GitHubUpdateCheckService

**Files:**
- Create: `src/SmartFileMerger/Infrastructure/WindowsFileSystemService.cs`
- Create: `src/SmartFileMerger/Infrastructure/GitHubUpdateCheckService.cs`
- Create: `tests/SmartFileMerger.Tests/Infrastructure/WindowsFileSystemServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: `IFileSystemService`, `IUpdateCheckService`, `Domain.Models`
- Produces: Working filesystem engine with Windows edge-case resilience and async update checker.

- [ ] **Step 1: Write integration tests for `WindowsFileSystemService`**
  - Test creating directories and querying relative paths.
  - Test moving a file and overwriting a target that has `FileAttributes.ReadOnly` set.
  - Test timestamp conservation: `LastWriteTimeUtc` on target matches source after move.
  - Test `RemoveEmptyDirectories`: deletes empty subdirectories, leaves non-empty subdirectories untouched.

- [ ] **Step 2: Run integration tests to verify they fail**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Infrastructure"`
  - Expected: FAIL.

- [ ] **Step 3: Implement `WindowsFileSystemService`**
  - Implement `MoveFile(source, target, overwrite)`:
    - If `overwrite && File.Exists(target)`: read attributes; if `ReadOnly` or `Hidden`, clear them via `File.SetAttributes`.
    - Capture `CreationTimeUtc` and `LastWriteTimeUtc` from source.
    - Perform `File.Move(source, target, overwrite)`.
    - Restore timestamps on target in safe `try/catch`.
  - Implement `RemoveEmptyDirectories`:
    - Recursive bottom-up pass deleting directories when `!Directory.EnumerateFileSystemEntries(dir).Any()`.

- [ ] **Step 4: Implement `GitHubUpdateCheckService`**
  - Use `HttpClient` with 1.5s timeout and `User-Agent: SmartFileMerger`.
  - Query `https://api.github.com/repos/mrambow/smart-file-merger/releases/latest`.
  - Safely parse JSON and compare `tag_name` with `AssemblyInformationalVersion` / `AssemblyVersion`.

- [ ] **Step 5: Run tests to verify they pass**
  - Run: `dotnet test --filter "FullyQualifiedName~SmartFileMerger.Tests.Infrastructure"`
  - Expected: PASS.

- [ ] **Step 6: Commit**
  ```bash
  git add src/SmartFileMerger/Infrastructure tests/SmartFileMerger.Tests/Infrastructure
  git commit -m "feat(infrastructure): implement WindowsFileSystemService and GitHubUpdateCheckService"
  ```

---

### Task 6: Presentation Layer: SpectreConsoleUserInterface

**Files:**
- Create: `src/SmartFileMerger/Presentation/SpectreConsoleUserInterface.cs`
- Modify: `tests/SmartFileMerger.Tests/SmartFileMerger.Tests.csproj`

**Interfaces:**
- Consumes: `IUserInterface`, `IConflictResolutionStrategy`, `Domain.Models`
- Produces: Complete Spectre.Console terminal UI.

- [ ] **Step 1: Implement `SpectreConsoleUserInterface`**
  - `ShowHeader`: Renders `FigletText("SmartFileMerger")` with Cyan1 style and subtitle.
  - `ShowUpdateNotice`: Renders warning panel with new release version and link.
  - `PromptConfiguration`: Prompts for Source and Target paths (auto-trimming quotes `"` and `'`), checks directory existence, confirms creation of missing target directory, prompts for `IsDryRun` (Normal vs. Simulation).
  - `ConfirmMerge`: Renders summary table of source, target, mode and asks for final `[j/N]` confirmation.
  - `RunWithProgress`: Wraps execution in `AnsiConsole.Progress()` with `ProgressBarColumn`, `SpinnerColumn`, `PercentageColumn`.
  - `PromptForConflictResolution`: Renders conflict panel with metadata diff table (`Table`) showing size and timestamp diffs with color coding (`[green]` for newer/larger, `[grey]` for older/smaller), followed by `SelectionPrompt<IConflictResolutionStrategy>`.
  - `DisplayMergeSummary`: Renders styled final summary panel with total files, moved, duplicates, resolved, skipped, errors, duration, and dry-run badge.

- [ ] **Step 2: Verify compilation and tests**
  - Run: `dotnet build`
  - Run: `dotnet test`
  - Expected: Build succeeded, all existing unit tests pass.

- [ ] **Step 3: Commit**
  ```bash
  git add src/SmartFileMerger/Presentation
  git commit -m "feat(presentation): implement SpectreConsoleUserInterface with diff tables and interactive wizard"
  ```

---

### Task 7: Program Entry Point, DI Wiring & End-to-End Verification

**Files:**
- Modify: `src/SmartFileMerger/Program.cs`
- Create: `tests/SmartFileMerger.Tests/EndToEndWorkflowTests.cs`

**Interfaces:**
- Consumes: All components via `IServiceCollection`
- Produces: Executable CLI tool.

- [ ] **Step 1: Wire Dependency Injection in `Program.cs`**
  ```csharp
  var services = new ServiceCollection();
  services.AddSingleton<IFileSystemService, WindowsFileSystemService>();
  services.AddSingleton<IUserInterface, SpectreConsoleUserInterface>();
  services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();
  services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();
  services.AddTransient<IConflictResolutionStrategy, MoveOrRenameTargetStrategy>();
  services.AddTransient<IConflictResolutionStrategy, RenameSourceStrategy>();
  services.AddTransient<IConflictResolutionStrategy, SkipConflictStrategy>();
  services.AddSingleton<ConflictResolutionRunner>();
  services.AddSingleton<MergeOrchestrator>();
  ```
  - In `Program.Main`:
    - Show header banner.
    - Check for updates asynchronously (non-blocking with cancellation token).
    - Prompt configuration via `IUserInterface`.
    - If confirmed, execute `MergeOrchestrator.ExecuteAsync`.
    - Display summary.

- [ ] **Step 2: Add End-to-End verification test**
  - Create `EndToEndWorkflowTests`:
    - Creates simulated directory hierarchy with 5 files:
      - 2 files unique in source (direct move).
      - 1 file identical in source and target (duplicate auto-resolve).
      - 1 file conflicting (source newer and larger).
      - 1 empty subdirectory.
    - Executes `MergeOrchestrator` with simulated user choice (overwrite).
    - Verifies all target files exist, source is clean, and `MergeSummary` counts match exactly.

- [ ] **Step 3: Run full verification test suite**
  - Run: `dotnet test --logger "console;verbosity=detailed"`
  - Expected: 100% tests PASS, 0 failures, 0 warnings.

- [ ] **Step 4: Commit**
  ```bash
  git add src/SmartFileMerger/Program.cs tests/SmartFileMerger.Tests/EndToEndWorkflowTests.cs
  git commit -m "feat: wire Program.cs with DI and add end-to-end workflow verification"
  ```
