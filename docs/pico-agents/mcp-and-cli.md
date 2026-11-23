<!-- Moved verbatim from PICO-UNITY-AGENTIC-TOOLS.AGENTS.md on 2026-11-22. In this project, skills/ paths are under .claude/skills/. -->

## CLI Conventions

This plugin drives the project through two CLIs. Pick the one that matches the task.

### Platform and Device Info

- Read the current project's platform and target device(s) from `.pico-cli/config.json` in the current path (`$PROJECT_ROOT/.pico-cli/config.json`), written after `/pico-unity-init` completes.
  - `mode` — the selected development/template mode: `picoxr`, `openxr`, or `picospatial`.
  - `platform` — the build platform (always `android` for PICO devices).
  - `devices` — the target PICO device(s), e.g. `pico swan`, `pico 4 ultra`.
- Only `pico_unity_init_completed: true` indicates that initialization has completed; file existence alone does not. When you need mode, platform, or device context, read it from here instead of asking the user again.

### `unity` CLI (Unity Hub)

- Used for editor/version/project lifecycle. Common commands:
  - `unity editors --installed` — list locally installed editors.
  - `unity install <version> -m android` — install a new editor bundled with Android Build Support.
  - `unity install-modules -e <version> -m android` — add the Android module to an already-installed editor (check the **Status** column before re-installing).
  - `unity projects add <path>` — register a project into the Unity project list.
  - `unity open <path> --build-target Android` — open the project and switch the Active Build Target to Android.
- **Target platform is always Android.** PICO devices are Android-based; do not build for Windows / macOS / WebGL.
- References:
  - Use Unity CLI: https://docs.unity.com/en-us/hub/use-unity-cli
  - Unity CLI reference: https://docs.unity.com/en-us/hub/unity-cli-reference
  - Release notes: https://docs.unity.com/en-us/hub/release-notes

### `pico-cli`

- Generic PICO CLI for command-family selection, help/version/setup discovery, output formats, device targeting, safe defaults, and first-pass troubleshooting.
- `pico-cli` also backs the `pico-dev-knowledge` MCP server (see below).

## MCP / Unity Editor Integration

The plugin's `.mcp.json` declares the knowledge server that loads automatically. A second, project-provided Unity Editor bridge supplies the scene tools used by the building-block skills:

- **`pico-dev-knowledge`** — A general knowledge-graph MCP server for PICO development, launched via `pico-cli`. It indexes documentation, API references, and best practices into a searchable graph.

  | Tool               | What it does                                                                                                                                          |
  | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `query_graph`      | Search the knowledge graph with natural-language questions or keywords. Supports `mode` (bfs/dfs), `depth` (1-6), and `token_budget` to bound output. |
  | `switch_workspace` | Hot-reload to a different version's knowledge data without restarting the server.                                                                     |

- **`unity`** — The Unity Editor MCP bridge, exposing the PICO MCP Extensions installed into the Unity project. It surfaces the nine `pico_xr_*` tools (`pico_xr_vst`, `pico_xr_controller`, `pico_xr_locomotion`, `pico_xr_spatial_mesh`, `pico_xr_plane`, `pico_xr_hand`, `pico_xr_grab`, `pico_xr_package`, `pico_xr_status`), which must be enabled under `Edit > Project Settings > AI > Unity MCP`. `pico-unity-buildingblocks` and `pico-unity-package-manager` operate entirely through these tools.
  - Requires the Unity Editor to be running with the project open and the bridge status **Running**.
  - Before any `pico_xr_*` workflow, compute its required tool set and verify every member is visible. Before the first mutation, include transitive dependency and post-write verification tools. If any are missing, stop and report the exact missing tool names; follow the detailed rules in `pico-unity-buildingblocks` and `pico-unity-package-manager`.
  - After every mutating call **that triggers a domain reload** (see the reload boundary above), run the post-write settle loop before the next MCP call.
