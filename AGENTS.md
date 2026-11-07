# Agent & Contributor Guidelines: SmartFileMerger

These guidelines are mandatory for all AI coding agents and human contributors working on `SmartFileMerger`.

---

## 1. Architectural Philosophy & Layering

The codebase is organized according to pragmatic Domain-Driven Design (DDD) and Clean Architecture principles:

```text
src/SmartFileMerger/
├── Domain/          # Core models, value objects, domain interfaces. Zero dependencies on external frameworks or I/O.
├── Application/     # Use case orchestration, conflict strategies, business workflows.
├── Infrastructure/  # Filesystem operations (System.IO, Win32), GitHub update check.
└── Presentation/    # Spectre.Console UI, command-line arguments, terminal display.
```

### Layer Constraints:
1. **Domain Layer**:
   - Pure C# records and value objects.
   - No dependencies on Application, Infrastructure, or Presentation.
   - Zero I/O operations (`System.IO`, `System.Console`, networking).
2. **Application Layer**:
   - Implements orchestration (`MergeOrchestrator`, `ConflictResolutionRunner`) and resolution strategies (`IConflictResolutionStrategy`).
   - **No Direct I/O Rule**: Domain and Application layers must NEVER call `System.IO` (e.g. `File`, `Directory`, `Path`, `FileInfo`) or `System.Console` directly.
   - All filesystem operations must be delegated to `IFileSystemService`.
   - All interactive UI interactions must be delegated to `IUserInterface`.
3. **Infrastructure Layer**:
   - Contains all concrete `System.IO` operations inside `WindowsFileSystemService`.
   - Handles Windows filesystem peculiarities (ReadOnly attributes, cross-volume timestamp retention, sharing violations).
4. **Presentation Layer**:
   - Handles user interaction via `Spectre.Console` in `SpectreConsoleUserInterface`.

---

## 2. Domain-Driven Design & Strongly-Typed Records

- **Avoid Primitive Obsession**:
  - Never pass raw strings for folder paths or relative paths. Use `DirectoryRoot` and `RelativeFilePath`.
  - Encapsulate path validation, normalization, and path-traversal checks inside the value objects at construction time.
- **Immutability**:
  - Domain models and metadata must be defined as immutable records (`public sealed record ...`).
  - Collections within models should be read-only (`IReadOnlyList<T>`, `IReadOnlyCollection<T>`).

---

## 3. The Ponytail Principle (Code Layout)

All methods and classes must adhere to the Ponytail principle:

1. **Table of Contents Structure**:
   - Public methods read like a high-level summary or table of contents.
   - Complex details, sub-steps, or helper algorithms are extracted into well-named private methods placed below the caller.
2. **Maximum Indentation Depth**:
   - Maximum nesting/indentation depth is **2**.
   - If code reaches 3 or more indentation levels, extract the inner block into a private method or invert conditions.
3. **Guard Clauses & Early Returns**:
   - Place guard clauses (`ArgumentNullException`, state checks, fast-path exits) at the very top of each method.
   - Avoid `else` ladders following an `if` that returns or throws.
4. **No Magic Values**:
   - No magic strings, string literals for logic, or arbitrary numeric constants. Use explicit constants, enums, or configuration fields.

---

## 4. Windows Filesystem Robustness Rules

When implementing or extending filesystem logic:

1. **ReadOnly Attributes on Overwrite**:
   - On Windows, overwriting a file marked with `FileAttributes.ReadOnly` throws `UnauthorizedAccessException`.
   - The filesystem service must explicitly strip `FileAttributes.ReadOnly` before calling `File.Move(..., overwrite: true)`.
2. **Cross-Volume Timestamp Retention**:
   - Moving or copying files across volumes (e.g., C: to D: or a network share/NAS) causes Windows to set the creation time to the time of copy.
   - Explicitly capture `CreationTimeUtc` and `LastWriteTimeUtc` from the source and re-apply them to the destination.
3. **Sharing Violations & File Locks**:
   - If an `IOException` (or `UnauthorizedAccessException`) occurs due to an active file lock by another process:
     - Catch the exception.
     - Log or record the failure in the `MergeSummary.Errors` collection.
     - Increment `ErrorsOccurred`.
     - Continue processing remaining files without terminating the entire merge operation.
4. **Recursion & Path Traversal Prevention**:
   - Validate that the target directory is not a subdirectory of the source directory (e.g., merging `C:\Data` into `C:\Data\SubFolder`), which would cause infinite recursion.
5. **Path Normalization**:
   - Clean drag-and-dropped paths that Windows users often paste with enclosing quotes (`"C:\Path"` or `'C:\Path'`).

---

## 5. Testing & Verification Standards

- **Test Naming Convention**:
  - Use `MethodOrUnit_Condition_ExpectedResult` (e.g., `FromFullPath_WhenPathHasEnclosingQuotes_StripsQuotesAndNormalizes`).
- **Frameworks & Tooling**:
  - Use `xUnit` for test runner and attributes (`[Fact]`, `[Theory]`, `[InlineData]`).
  - Use `FluentAssertions` for assertions (`result.Should().BeTrue()`).
  - Use `NSubstitute` for mocking interfaces (`Substitute.For<IFileSystemService>()`).
- **No Hollow Stubs**:
  - Never generate dummy or unasserted tests. Every test must verify genuine business requirements and edge cases.
  - All tests must pass cleanly (`dotnet test`) with 0 warnings before opening a PR or claiming task completion.

---

## 6. Code Review & Quality Gates (CodeRabbit)

- **AI Code Review via CodeRabbit**:
  - Pull requests are reviewed using CodeRabbit configured in `.coderabbit.yaml`.
  - **Maintainer Gate & Misuse Prevention**: To prevent unauthorized token/credit consumption and unintended execution on untrusted PRs, automatic reviews on new PRs are disabled (`auto_review.enabled: false`). CodeRabbit is strictly opt-in and triggers only when the maintainer adds the `review` label to the pull request or runs a manual trigger command. Once the label is active, incremental reviews automatically check subsequent commits up to a safeguard limit of 5 commits (`auto_pause_after_reviewed_commits: 5`).
  - Reviews enforce clean architecture, the Ponytail principle, Windows filesystem robustness rules, and test verification standards.

---

## 7. Automated Release Workflow & Versioning

- **Fully Automated Releases on Merge**:
  - When a pull request is merged into `main`, GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)) automatically triggers the release pipeline.
  - **Semantic Tagging**: The workflow determines the release tag (`v1.0.0` initially, followed by automated patch/minor/major increments based on conventional commit messages like `feat:` / `breaking:` or manual dispatch).
  - **Binary & Checksum Generation**: Compiles the standalone self-contained `SmartFileMerger.exe` for `win-x64` with single-file compression and native libraries included, embeds the assembly version, and calculates the SHA256 checksum (`SmartFileMerger.exe.sha256`).
  - **GitHub Release Publication**: Automatically publishes the release to GitHub Releases with generated release notes and attached binary assets.
  - **Bypassing & Manual Overrides**: Adding `[skip release]` or `[no release]` to a commit message skips release generation. Manual releases can also be triggered via `workflow_dispatch` in GitHub Actions or by pushing a `v*` tag directly.
