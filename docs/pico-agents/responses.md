<!-- Moved verbatim from PICO-UNITY-AGENTIC-TOOLS.AGENTS.md on 2026-11-22. In this project, skills/ paths are under .claude/skills/. -->

## Working Principles

- Use the most relevant skill first, then read only the references needed for the task.
- Always run the Unity MCP tool-set pre-check before the first `pico_xr_*` call, expand it with transitive dependencies before the first mutation, and never start a partial workflow when any required tool is missing.
- After a mutating MCP action **that triggers a domain reload** (see the reload boundary above), run the settle loop before chaining the next MCP call; the Editor is reloading.
- Resolve dependencies from the outside in for `enable` / `configure` actions only; skip dependency resolution for `disable` / `status`.
- Keep the target platform on Android for all Unity operations.
- Do not hand-edit `Packages/manifest.json`; do not invent SDK APIs or hard-code package versions unless the user asks. The only exception is `pico-unity-init` bootstrapping the manifest before the MCP bridge exists; every post-init package change goes through `pico_xr_package`.
- Do not repair a general installed dependency by re-adding the same moving Git URL. Treat `already_present` as no change and require a user-supplied different immutable tag or commit. The only exception is the guarded official PICO Unity SDK repair in `pico-unity-package-manager` §4.6, which reuses the exact captured recognized official moving Git URL and verifies the requested outcome after the settle loop.

## Response Pattern

- Spatial work: report the selected skill, mode and package evidence, tool availability when scene editing is needed, files/components changed, and verification performed. Distinguish read-only advice, script generation, Editor execution, and device testing; never imply a runtime test occurred from static inspection alone. For scene changes, end with the save result or the reason saving is blocked.
- Building-block work: open with one line stating the action, stream progress as a checklist (✓ / … / ✗) so auto-installs are visible, and close with the checklist. For mutating flows, the last line must reflect Save Scene. Only echo the full `pico_xr_status` table when the request itself is a status query.
- Package work: echo the result `summary`; on `already_present` say "no change made"; on `skipped` echo the `warning`. Treat a missing dependency as transitional only inside an already-authorized install/import or building-block dependency flow; follow package-manager §4.1-§4.3 and never turn a read-only query into an install. For other `skipped` results, propose the fix and wait. On `error`, echo the error and stop instead of retrying blindly.
- Repository-wide or response-style `skipped` rules must never override these typed transitional exceptions with a blanket `skipped` → stop rule.
- Classify every `skipped` by action, runtime, and workflow context. The only auto-recoverable cases are the documented Spatial Mesh / Plane / Hand first-enable import-recompile stages and package/sample dependency resolution within an already-authorized write flow. The first-enable allowlist is `spatial_mesh:SpatialMeshManager`, `plane:PlaneDetectionManager`, `hand:HandVisualizer`, and `hand:Hands Interaction Demo`; derive it from the called tool plus the exact imported item in `detail`/`warning`, not from a generic summary. Run one bounded settle/retry per distinct documented transition. A repeated `skipped` for the same transition means no progress and must stop and ask, while OpenXR Hand may legitimately advance through its two distinct sample stages. A read-only `info` query reporting "not installed" is a final query result, not permission to install. Always stop on `error`.

## Delivery Checklist

- selected skill and why it was chosen
- MCP pre-check result and any settle-loop waits
- packages/samples installed or changed and their outcome (new / already present / updated)
- feature blocks enabled/disabled/configured and the resulting state
- whether the scene was saved
- verification steps and expected results
