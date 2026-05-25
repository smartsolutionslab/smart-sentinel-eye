#!/usr/bin/env python3
"""Convert tasks.md into GitHub issues attached to the Smart Sentinel Eye project.

This is a one-shot helper for the Spec-Kit `/speckit-taskstoissues`
equivalent — it parses a tasks.md, derives labels from the [STORY]
marker, and creates one issue per task.

Idempotent guard: refuses to run if any issue already references the
same task ID via the title prefix. Use --force to override.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

TASK_LINE = re.compile(
    r"^- \[ ] \*\*T(?P<id>\d+) (?:\[(?P<parallel>P)\] )?\[(?P<story>[A-Z0-9]+)\]\*\* (?P<desc>.+)$"
)

STORY_TO_PHASE_LABEL = {
    "SETUP": "phase:setup",
    "FOUND": "phase:foundational",
    "US1": "phase:us1",
    "US2": "phase:us2",
    "POLISH": "phase:polish",
}


@dataclass(frozen=True)
class Task:
    identifier: str  # e.g. "T001"
    story: str  # e.g. "SETUP"
    parallel: bool
    description: str


def parse_tasks(tasks_md: Path) -> list[Task]:
    tasks: list[Task] = []
    for line in tasks_md.read_text(encoding="utf-8").splitlines():
        match = TASK_LINE.match(line.strip())
        if match is None:
            continue
        tasks.append(
            Task(
                identifier=f"T{match.group('id')}",
                story=match.group("story"),
                parallel=match.group("parallel") == "P",
                description=match.group("desc"),
            )
        )
    return tasks


def existing_issue_titles(owner: str, repo: str) -> set[str]:
    """Return the set of issue titles in the repo so we can skip duplicates."""
    output = subprocess.check_output(
        [
            "gh",
            "issue",
            "list",
            "--repo",
            f"{owner}/{repo}",
            "--state",
            "all",
            "--limit",
            "1000",
            "--json",
            "title",
        ],
        text=True,
    )
    return {item["title"] for item in json.loads(output)}


def create_issue(
    *,
    owner: str,
    repo: str,
    feature_branch: str,
    feature_slug: str,
    project_title: str,
    task: Task,
) -> str:
    title = f"[{task.identifier}] {task.description.split('.')[0].strip()}"
    if len(title) > 120:
        title = title[:117] + "..."
    phase_label = STORY_TO_PHASE_LABEL.get(task.story)
    if phase_label is None:
        raise SystemExit(f"Unknown story '{task.story}' for task {task.identifier}")
    body_lines = [
        f"**Task {task.identifier}** from `specs/{feature_branch}/tasks.md`.",
        "",
        task.description,
        "",
        f"**Phase / Story:** {task.story}",
        f"**Parallelisable:** {'yes' if task.parallel else 'no'}",
        f"**Feature:** {feature_branch}",
        "",
        f"See [tasks.md](https://github.com/{owner}/{repo}/blob/{feature_branch}/specs/{feature_branch}/tasks.md) for full context, dependencies, and parallel opportunities.",
    ]
    body = "\n".join(body_lines)
    result = subprocess.run(
        [
            "gh",
            "issue",
            "create",
            "--repo",
            f"{owner}/{repo}",
            "--title",
            title,
            "--body",
            body,
            "--label",
            f"task,{feature_slug},{phase_label}",
            "--project",
            project_title,
        ],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise SystemExit(f"Failed to create issue for {task.identifier}: {result.stderr}")
    return result.stdout.strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tasks_md", type=Path, help="Path to tasks.md")
    parser.add_argument("--owner", default="smartsolutionslab")
    parser.add_argument("--repo", default="smart-sentinel-eye")
    parser.add_argument("--feature-branch", required=True, help="e.g. 001-register-camera")
    parser.add_argument(
        "--feature-slug",
        required=True,
        help="Label slug for the feature, e.g. feature:001-register-camera",
    )
    parser.add_argument("--project-title", default="Smart Sentinel Eye")
    parser.add_argument("--force", action="store_true", help="Bypass the duplicate-title guard")
    parser.add_argument("--dry-run", action="store_true", help="Print actions without creating issues")
    args = parser.parse_args()

    tasks = parse_tasks(args.tasks_md)
    if not tasks:
        raise SystemExit(f"No task lines parsed from {args.tasks_md}")

    print(f"Parsed {len(tasks)} tasks from {args.tasks_md}")

    if not args.force:
        existing = existing_issue_titles(args.owner, args.repo)
        for task in tasks:
            prefix = f"[{task.identifier}]"
            if any(title.startswith(prefix) for title in existing):
                raise SystemExit(
                    f"An issue already exists for {task.identifier}; re-run with --force to override."
                )

    for task in tasks:
        if args.dry_run:
            print(f"[dry-run] {task.identifier} -> {task.description[:80]}")
            continue
        url = create_issue(
            owner=args.owner,
            repo=args.repo,
            feature_branch=args.feature_branch,
            feature_slug=args.feature_slug,
            project_title=args.project_title,
            task=task,
        )
        print(f"{task.identifier:5}  {url}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
