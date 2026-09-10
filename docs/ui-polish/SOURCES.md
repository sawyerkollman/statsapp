# Source grounding and configuration notes

Prepared 2026-09-06. Repository baseline rechecked against `master`: `ad5ec1d88573a5ca344e0585d4e6556dcf5051fc`. Compare with current HEAD before implementing. This handoff is a proposed design, not a report of a rendered UI or working configuration on the user's client.

## Repository evidence

Base: https://github.com/sawyerkollman/statsapp/tree/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc

| Source | Relevant observation |
| --- | --- |
| [CLAUDE.md](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/CLAUDE.md) | WPF/.NET 8 architecture; thread/safety rules; zero-warning build/tests; Windows launch constraints |
| [README](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/README.md) | Existing features and expected user-visible behavior |
| [DashboardWindow.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Views/DashboardWindow.xaml) | Flat toolbar; grouped WrapPanel tiles; 440-unit Metrics/Settings flyout; long settings content |
| [TileTemplates.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Views/TileTemplates.xaml) | Labels/footer at small sizes; differing main-value sizes; hover menu; chart/gauge/value templates |
| [TileSizeToLengthConverter.cs](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Converters/TileSizeToLengthConverter.cs) | Existing S 150×70, M 215×120, L 440×160 |
| [Theme.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Views/Theme.xaml) and [ThemeManager.cs](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Helpers/ThemeManager.cs) | Live resource replacement and fixed overlay palette constraints |
| [App.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/App.xaml) | Dictionary order, shared styles, tab and textbox styling |
| [FansWindow.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Views/FansWindow.xaml) | Profile/master control, warnings, game mode, per-channel modes and curve editor |
| [PeaksWindow.xaml](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Views/PeaksWindow.xaml) | Fixed numeric columns and narrow window constraints |
| [Stats.App.csproj](https://github.com/sawyerkollman/statsapp/blob/ad5ec1d88573a5ca344e0585d4e6556dcf5051fc/src/Stats.App/Stats.App.csproj) | Native Windows/WPF target, app manifest, pinned tray dependency |

The implementation agent must also read the relevant feature specs in `docs/superpowers/specs/` before modifying a subsystem. This package does not replace those technical contracts.

## Official Codex documentation

- [Subagents and custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents): local project TOML role files; explicit model/effort; child defaults; inheritance and role precedence; concurrent child cap. Custom local files are not assumed to configure hosted Work.
- [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference): supported project settings, including `agents.enabled`, `agents.default_subagent_model`, `agents.default_subagent_reasoning_effort`, and `agents.max_concurrent_threads_per_session`.
- [Models](https://learn.chatgpt.com/docs/models): current model families. The selected concrete IDs are `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, and optional parent `gpt-6-astra`; verify target-account availability.

Some documentation examples use the shorter `gpt-5.6` name. This package deliberately uses the explicit `gpt-5.6-sol` ID exposed in the preparation runtime and current model documentation. No model availability or exact billing savings is guaranteed for another account/client.

The hosted spawn examples reflect the `collaboration.spawn_agent` schema advertised in the preparation session: model/effort overrides require a bounded or no-history fork rather than full history. Future agents must inspect their own tool schema.

## Validation performed while preparing the package

Only documentation/configuration packaging checks were performed: syntax parsing, task/dependency/role consistency, relative document links, and archive integrity. No application code was changed. The Codex executable was absent, so no local agent discovery or model-routing execution was tested. No Windows UI was launched, no hardware was read, and no application build/test outcome is asserted.
