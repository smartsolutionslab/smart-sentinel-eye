#!/usr/bin/env python3
"""
Parse a Spec-Kit tasks.md and create one GitHub issue per task via `gh`.

Usage:
    python scripts/spec-kit-tasks-to-issues.py specs/002-watch-camera-live/tasks.md \
        --feature-label "feature:002-watch-camera-live" \
        [--dry-run]

Task lines look like:
    - [ ] **T001 [P] [FOUND]** Description that may span lines until the next task.

Each task becomes:
    title: "[T001] First ~80 chars of description"
    body:  Full description + back-links to spec.md / plan.md / tasks.md
    labels: feature-label + story-bucket label (FOUND / US1 / US2 / POLISH / SETUP)

Re-running is safe — issues with the same `[Tnnn]` prefix are skipped.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

TASK_PATTERN = re.compile(
    r"^- \[ \] \*\*(T\d+)\s+(?:\[(P)\]\s+)?\[(\w+)\]\*\*\s+(.*)$"
)


@dataclass
class Task:
    id: str
    parallel: bool
    story: str
    description: str

    @property
    def title(self) -> str:
        first_line = self.description.split("\n", 1)[0].strip()
        if len(first_line) > 110:
            first_line = first_line[:107].rstrip() + "..."
        return f"[{self.id}] {first_line}"


def parse_tasks(tasks_md: Path) -> list[Task]:
    tasks: list[Task] = []
    current: Task | None = None
    in_code = False
    for raw in tasks_md.read_text(encoding="utf-8").splitlines():
        stripped = raw.rstrip()
        if stripped.startswith("```"):
            in_code = not in_code
            if current is not None:
                current.description += "\n" + raw
            continue
        if in_code:
            if current is not None:
                current.description += "\n" + raw
            continue

        match = TASK_PATTERN.match(stripped)
        if match:
            if current is not None:
                tasks.append(current)
            ident, parallel_flag, story, desc = match.groups()
            current = Task(
                id=ident,
                parallel=parallel_flag == "P",
                story=story,
                description=desc.strip(),
            )
            continue

        if stripped.startswith(("---", "#")) or (stripped == "" and current is not None and not current.description.endswith("\n")):
            if current is not None:
                tasks.append(current)
                current = None
            continue

        if current is not None and stripped:
            current.description += "\n" + raw
        elif current is not None and stripped == "":
            current.description += "\n"

    if current is not None:
        tasks.append(current)
    return tasks


def existing_issue_numbers(feature_label: str) -> set[str]:
    """Return the set of `Tnnn` task IDs already covered by issues with this label."""
    result = subprocess.run(
        [
            "gh",
            "issue",
            "list",
            "--label",
            feature_label,
            "--state",
            "all",
            "--limit",
            "500",
            "--json",
            "title",
        ],
        capture_output=True,
        text=True,
        check=True,
    )
    items = json.loads(result.stdout or "[]")
    ids = set()
    for item in items:
        match = re.match(r"^\[(T\d+)\]", item.get("title", ""))
        if match:
            ids.add(match.group(1))
    return ids


def ensure_label(name: str, color: str, description: str) -> None:
    subprocess.run(
        ["gh", "label", "create", name, "--color", color, "--description", description],
        capture_output=True,
        text=True,
        check=False,
    )


def create_issue(task: Task, feature_label: str, spec_dir: str, dry_run: bool) -> None:
    story_label = f"story:{task.story.lower()}"
    body_lines = [
        task.description.strip(),
        "",
        "---",
        "",
        f"- Story bucket: **{task.story}**",
        f"- Parallelisable: **{'yes' if task.parallel else 'no'}**",
        f"- Spec: [{spec_dir}/spec.md](../{spec_dir}/spec.md)",
        f"- Plan: [{spec_dir}/plan.md](../{spec_dir}/plan.md)",
        f"- Tasks: [{spec_dir}/tasks.md](../{spec_dir}/tasks.md)",
    ]
    body = "\n".join(body_lines)

    cmd = [
        "gh",
        "issue",
        "create",
        "--title",
        task.title,
        "--body",
        body,
        "--label",
        feature_label,
        "--label",
        story_label,
    ]
    if dry_run:
        print(f"DRY-RUN  {task.id}  {task.title}")
        return
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"FAIL     {task.id}: {result.stderr.strip()}", file=sys.stderr)
    else:
        print(f"CREATED  {task.id}  {result.stdout.strip()}")


STORY_LABEL_COLORS = {
    "FOUND": ("0e8a16", "Foundational task (Spec-Kit story bucket)"),
    "US1": ("1d76db", "User Story 1 task (Spec-Kit story bucket)"),
    "US2": ("1d76db", "User Story 2 task (Spec-Kit story bucket)"),
    "US3": ("1d76db", "User Story 3 task (Spec-Kit story bucket)"),
    "POLISH": ("fbca04", "Polish task (Spec-Kit story bucket)"),
    "SETUP": ("c5def5", "Setup task (Spec-Kit story bucket)"),
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("tasks_md", type=Path, help="Path to tasks.md")
    parser.add_argument("--feature-label", required=True, help="e.g. feature:002-watch-camera-live")
    parser.add_argument("--dry-run", action="store_true", help="Print what would be done without calling gh")
    args = parser.parse_args()

    if not args.tasks_md.exists():
        print(f"Not found: {args.tasks_md}", file=sys.stderr)
        return 1

    tasks = parse_tasks(args.tasks_md)
    if not tasks:
        print("No tasks parsed.", file=sys.stderr)
        return 1
    print(f"Parsed {len(tasks)} tasks from {args.tasks_md}")

    spec_dir = args.tasks_md.parent.name

    if not args.dry_run:
        for story, (color, desc) in STORY_LABEL_COLORS.items():
            ensure_label(f"story:{story.lower()}", color, desc)
        existing = existing_issue_numbers(args.feature_label)
        if existing:
            print(f"Skipping {len(existing)} already-created task IDs: {sorted(existing)}")
    else:
        existing = set()

    for task in tasks:
        if task.id in existing:
            continue
        create_issue(task, args.feature_label, spec_dir, args.dry_run)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
