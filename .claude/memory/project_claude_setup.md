---
name: project-claude-setup
description: Claude Code config files already set up in this repo
metadata:
  type: project
---

As of 2026-06-17: repo has `CLAUDE.md` (from /init) and `.claude/settings.json` (allow-list for dotnet build/run/restore/ef migrations+database, read-only git status/diff/log). `.gitignore` updated to exclude `.claude/settings.local.json`. No `.claude/commands/` yet — offered, not confirmed.

**Why:** avoid recreating these or re-asking in future sessions.
**How to apply:** check current state of these files before assuming gaps; if asked for "recommended Claude files" again, these are done — ask what's still missing instead of redoing.
