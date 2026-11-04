# SmartFileMerger

[![CI](https://github.com/mrambow/smart-file-merger/actions/workflows/ci.yml/badge.svg)](https://github.com/mrambow/smart-file-merger/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

**SmartFileMerger** is a robust, interactive Windows console application built in .NET 10 (C# 14) for safely and cleanly merging two directory trees (`Source / A` into `Target / B`).

Unlike basic file copy tools, SmartFileMerger is designed for real-world scenarios: large messy directory trees, cross-drive moves, file conflicts, active file locks, read-only files, and duplicate file handling without endless prompting.

---

## Key Features

1. **Hierarchy & Structure Preservation**  
   Files are transferred while strictly preserving their relative folder hierarchy from the source root into the target directory.

2. **Non-Blocking Conflict Collection**  
   Files that do not conflict are moved immediately in a fast bulk phase. Files that already exist at the destination are queued in a conflict backlog rather than stopping or interrupting execution.

3. **Automated Duplicate Detection**  
   Files with identical byte size and last-write timestamp (to the second) are automatically recognized as identical duplicates and resolved safely without redundant interactive prompts.

4. **Interactive Conflict Resolution & Diff Table**  
   At the end of the merge phase, all collected conflicts are presented in an interactive [Spectre.Console](https://spectreconsole.net/) review table displaying size and modification timestamp diffs with color coding. Users can choose from 4 resolution strategies:
   - **Overwrite Target**: Replaces the destination file with the source file.
   - **Move & Backup Target**: Renames the existing target file with a timestamp/counter suffix and moves the source in.
   - **Rename Source**: Moves the source file into the destination folder with a modified filename suffix.
   - **Skip**: Leaves both files unchanged.

5. **Windows Filesystem Resilience**  
   - Automatically strips `FileAttributes.ReadOnly` on destination files before overwrite moves to prevent `UnauthorizedAccessException`.
   - Explicitly restores `CreationTimeUtc` and `LastWriteTimeUtc` when moving files across volume or drive boundaries (e.g., local SSD to NAS/USB).
   - Gracefully handles file locks (`IOException` sharing violations) by logging errors and continuing with the next item instead of crashing.

6. **Interactive Wizard & Simulation Mode (`--dry-run`)**  
   Run interactively with path autocomplete and prompts, or pass CLI arguments. With `--dry-run`, test the entire merge operation without writing or moving any files on disk.

7. **GitHub Update Notification**  
   Non-blocking check against GitHub Releases at startup with an unobtrusive notice when a newer version is available.

---

## Architecture Overview

SmartFileMerger follows a pragmatic Clean Architecture / Domain-Driven Design pattern:

```text
src/SmartFileMerger/
├── Domain/
│   ├── Models/         # Strongly-typed records: DirectoryRoot, RelativeFilePath, FileConflict, etc.
│   └── Interfaces/     # IFileSystemService, IUserInterface, IConflictResolutionStrategy, etc.
├── Application/        # MergeOrchestrator, ConflictResolutionRunner, Strategy implementations
├── Infrastructure/     # WindowsFileSystemService (System.IO, Win32 calls), GitHubUpdateCheckService
└── Presentation/       # SpectreConsoleUserInterface (terminal prompts, tables, formatting)
```

- **Strict Layer Isolation**: The Domain and Application layers contain zero direct calls to `System.IO` or `System.Console`. All I/O is abstracted behind testable interfaces.
- **Strong Typing**: Directory paths and relative file paths are encapsulated in value objects (`DirectoryRoot` and `RelativeFilePath`) with validation and normalization.
- **Ponytail Principle**: Public methods read like a table of contents with early returns, guard clauses at the top, and maximum indentation depth of 2.

---

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Windows 10, Windows 11, or Windows Server 2022+

### Installation & Run

Clone the repository:
```bash
git clone https://github.com/mrambow/smart-file-merger.git
cd smart-file-merger
```

Run in interactive wizard mode:
```bash
dotnet run --project src/SmartFileMerger
```

Run with CLI parameters:
```bash
dotnet run --project src/SmartFileMerger -- "C:\Path\To\Source" "D:\Path\To\Target"
```

Run in simulation mode (no disk modifications):
```bash
dotnet run --project src/SmartFileMerger -- "C:\Path\To\Source" "D:\Path\To\Target" --dry-run
```

---

## Development & Testing

Build the solution:
```bash
dotnet build
```

Run the unit and integration test suite:
```bash
dotnet test
```

---

## Governance & Contributing

- Code style and formatting are enforced via [.editorconfig](.editorconfig).
- Contributor guidelines and AI agent rules are specified in [AGENTS.md](AGENTS.md).
- Automated CI pipeline runs on push and PRs via GitHub Actions.
- Maker-checker security gate uses `claude-pr-gate` triggered on `review` label.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
