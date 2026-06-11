# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Repository Is

A personal notes and technical writing repo for a Unity game development article series published on 知乎 (Zhihu). The series is titled **"Unity 游戏框架从零搭建"**, targeting Unity beginner-to-intermediate C# developers.

This is **not a buildable project**. There are no build commands, test suites, or package managers.

## Repository Layout

```
ToolkitInPorject/     # Main article drafts and source material
  *_Zhihu.md          # Published article drafts
  EnumEventSystem.cs  # Canonical Unity C# implementation (source of truth for article code)
  Resources Kit *.md  # Reference notes for resource management articles
  UI Kit.md           # Reference notes for future UI framework article
  EventKit.md         # Reference notes for future EventBus article
  Config 配置表.md    # Stub for future config table article
DesignerPartterns/    # Design pattern reference notes (GoF 23 patterns)
Standard/             # Unity project/code conventions (PascalCase, _camelCase fields, etc.)
AI/                   # Style references
  technicla_writer.txt # Writing system prompt — defines article voice and structure
docs/superpowers/specs/ # Article design specs (created before writing each article)
```

## Article Series Plan

| # | Topic | Status | File |
|---|-------|--------|------|
| 1 | 对象池 (Object Pool) | Complete | `ObjectPool_Zhihu.md` |
| 2 | EventBus 事件系统 | Complete | `EventBus_Zhihu.md` |
| 3 | AssetBundle 资源热更 | Complete | `AssetBundle_Zhihu.md` |
| 4 | YooAsset 资源框架 | Complete | `YooAsset_Zhihu.md` |
| 番外 | Unity 内存管理 | Complete | `UnityMemory_Zhihu.md` |
| 5 | 配置表 (ScriptableObject config) | Not started | — |
| 6 | UI 框架 | Not started | — |

## Writing Conventions

- **Language**: Simplified Chinese, natural and professional tone
- **Narrative style**: Problem-driven, from-scratch derivation — naive approach first, then polished solution
- **Design patterns**: Named explicitly (Observer, Strategy, Command, Dependency Inversion, etc.) with a brief explanation of why that pattern was chosen, woven into the relevant section body — not isolated
- **Pattern reference book**: 《游戏编程模式》(Game Programming Patterns) — cite by chapter where relevant
- **Code accuracy**: Article code must match the actual Unity implementation files (e.g., `EnumEventSystem.cs` is the canonical source; do not deviate)
- **Comments in code**: Minimal — only when a non-obvious constraint or design decision needs calling out
- **Style guide**: `AI/technicla_writer.txt` defines the voice and blog format (hook → problem → solution → implementation → gotchas)

## Workflow for Each New Article

1. Create a spec in `docs/superpowers/specs/YYYY-MM-DD-<topic>-article-design.md`
2. Get user approval on section structure before writing
3. Write the article draft to `ToolkitInPorject/<Topic>_Zhihu.md`
4. Cross-check all code snippets against the canonical `.cs` implementation files

## Code Style (from `Standard/项目规范.md`)

- Classes, methods, properties: `PascalCase`
- Private fields: `_camelCase` (underscore prefix)
- Interfaces: `I` prefix (`IPoolItem`, `IUnSubscribe`)
- Enums and their members: `PascalCase`
- Events: `On` prefix or past-tense verb

## Important Constraints

- **No git operations** — work locally only, never run git commands
- `verbose: true` is active — explain reasoning and planned changes before writing
