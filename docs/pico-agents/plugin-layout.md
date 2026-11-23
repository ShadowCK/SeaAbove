<!-- Moved verbatim from PICO-UNITY-AGENTIC-TOOLS.AGENTS.md on 2026-11-22. In this project, skills/ paths are under .claude/skills/. -->

# PICO Unity Agentic Tools Plugin

This repository is a skills plugin for AI coding agents: Claude Code, Cursor, Codex, GitHub Copilot, Trae CLI, OpenCode V2, CodeBuddy Code, Qoder CLI, Antigravity CLI, and Grok CLI.

## Repository Overview

The plugin provides domain-specific guidance for building and maintaining **PICO OS 6** applications in **Unity**.
It ships two kinds of capability:

- **Skills** under `skills/` — prompts plus bundled references for project initialization, feature orchestration, and package management inside a Unity project.
- **CLI capability** — conventions for driving the project through CLI, covering both the `unity` CLI (Unity Hub) and `pico-cli`.

Skills here are not code libraries. They are prompts plus bundled references for implementation and diagnosis work.

## Host Integration Points

- Claude Code: `.claude-plugin/plugin.json`
- Codex: `.codex-plugin/plugin.json`
- Cursor: `.cursor-plugin/plugin.json`
- GitHub (Copilot/Workflow integrations): `.github/plugin/plugin.json`
- CodeBuddy Code: `.codebuddy-plugin/plugin.json`
- Qoder CLI: `.qoder-plugin/plugin.json`
- Grok CLI: `.grok-plugin/plugin.json`; Grok discovers `skills/` and MCP by convention, so its manifest declares only plugin identity
- Antigravity CLI: the plugin-level `plugin.json` at this directory's root
- Trae CLI: local marketplace installation driven by `pico-cli setup` / `pico-cli plugin update`
- OpenCode V2: no plugin manifest; `pico-cli setup` writes Skills and MCP entries into the OpenCode configuration
- MCP config: `.mcp.json`
- Hosts should import this directory as the plugin root and resolve `skills/` and `.mcp.json` using the relative paths defined in the host manifest.

## Published Layout

```text
.
├── .claude-plugin/                 # Claude Code plugin metadata
├── .codex-plugin/                  # Codex plugin metadata
├── .cursor-plugin/                 # Cursor plugin metadata
├── .github/plugin/                 # GitHub plugin metadata
├── .codebuddy-plugin/              # CodeBuddy Code plugin metadata
├── .qoder-plugin/                  # Qoder CLI plugin metadata
├── .grok-plugin/                   # Grok CLI plugin metadata
├── skills/
│   ├── pico-unity-init/            # One-time PICO Unity project initialization wizard
│   ├── pico-unity-buildingblocks/  # PICO XR building-block orchestration via pico_xr_* MCP tools
│   ├── pico-unity-package-manager/ # Unity Package Manager package/sample subsystem via pico_xr_package
│   ├── pico-unity-spatial/        # PICO Spatial setup and feature routing
│   ├── spatialadapter-runtime-overview/ # Spatial Adapter API discovery
│   ├── spatialadapter-scene-setup/ # Active scene SpatialCamera setup
│   ├── spatialadapter-camera-window-api/ # Camera and window configuration
│   ├── spatialadapter-spatial-camera-focus/ # Target following within camera bounds
│   ├── spatialadapter-input-api/  # Spatial Input and manipulation
│   ├── spatialadapter-components-api/ # Native components and synchronization
│   ├── spatialadapter-runtime-core-api/ # Runtime initialization and resource APIs
│   ├── spatialml-debugging/        # Service logging and global tensor probes
│   └── spatialml/                  # Unity SpatialML workflow with Pipeline Zoo package reference
│
├── .mcp.json                       # MCP server declaration (pico-dev-knowledge)
├── plugin.json                     # Plugin-level manifest; also the Antigravity CLI entry point
├── AGENTS.md                       # This file
├── CLAUDE.md                       # Claude-facing entry file
└── README.md                       # Plugin overview and installation notes
```
