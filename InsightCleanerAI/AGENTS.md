# Repository Guidelines

## Project Structure & Module Organization
This repository is a single WPF desktop app targeting `net5.0-windows`. Root-level XAML files such as `App.xaml`, `MainWindow.xaml`, `SettingsWindow.xaml`, and `BlacklistWindow.xaml` define the UI, with matching `.xaml.cs` code-behind files beside them. Keep reusable logic out of the windows when possible.

`Models/` contains scan and AI domain models. `ViewModels/` holds MVVM state; `MainViewModel` is split across `MainViewModel.cs`, `MainViewModel.Settings.cs`, and `MainViewModel.Storage.cs`. `Services/` contains filesystem scanning and AI providers, `Persistence/` wraps SQLite storage, `Infrastructure/` holds config, logging, converters, and helpers, and `Resources/Strings.resx` stores user-facing strings.

## Build, Test, and Development Commands
Use Windows with a .NET SDK compatible with `net5.0-windows`.

- `dotnet restore InsightCleanerAI.csproj` — restore NuGet packages such as `Microsoft.Data.Sqlite`.
- `dotnet build InsightCleanerAI.csproj -c Debug` — compile the desktop app.
- `dotnet run --project InsightCleanerAI.csproj` — launch the app locally.
- `dotnet clean InsightCleanerAI.csproj` — clear build output before a fresh rebuild.

No automated test project is committed today, so build verification and manual smoke testing are the current baseline.

## Coding Style & Naming Conventions
Follow the existing C# style: 4-space indentation, braces on new lines, and descriptive names. Use `PascalCase` for public types, properties, and methods; `camelCase` for locals and parameters; `_camelCase` for private fields. Nullable reference types are enabled, so avoid suppressing warnings without a clear reason.

Keep MVVM boundaries intact: put reusable logic in `Services/`, `Infrastructure/`, or `Persistence/`, not in XAML code-behind. Update `Resources/Strings.resx` instead of hardcoding UI text.

## Testing Guidelines
Before opening a PR, verify that the project builds and manually test the main flows: folder scanning, settings persistence, SQLite-backed history, and AI mode switching. If you add testable logic, prefer extracting it from UI code so it can later live under a dedicated `InsightCleanerAI.Tests` project.

## Commit & Pull Request Guidelines
Recent history uses short, action-oriented subjects in English or Chinese, for example `Update README.md`, `add version number`, and `核心内容已全部更新`. Keep commits small and imperative; add scope when useful, such as `Services: improve model loading`.

PRs should explain the user-visible change, list validation steps, link related issues, and include screenshots or GIFs for WPF UI changes.

## Security & Configuration Tips
Do not commit API keys, local paths, or generated data. Runtime settings, cache, SQLite data, and logs are written under `%AppData%\InsightCleanerAI\` (for example `settings.json`, `insights.db`, and `logs\debug.log`) and should stay local.
