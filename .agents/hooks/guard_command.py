import json
import os
import re
import subprocess
import sys

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


def allow() -> None:
    print(json.dumps({"decision": "allow"}))


def deny(reason: str) -> None:
    print(json.dumps({"decision": "deny", "reason": reason}))


def current_git_branch(cwd: str) -> str:
    try:
        res = subprocess.run(
            ["git", "rev-parse", "--abbrev-ref", "HEAD"],
            cwd=cwd or None,
            capture_output=True,
            text=True,
            timeout=2,
            creationflags=CREATE_NO_WINDOW,
        )
        if res.returncode == 0:
            return res.stdout.strip()
    except Exception:
        pass
    return ""


def split_segments(cmd: str) -> list[str]:
    return [s.strip() for s in re.split(r"(?:&&|\|\||;|\||\r?\n)+", cmd) if s.strip()]


def tokenize(segment: str) -> list[str]:
    return re.findall(r'"[^"]*"|\'[^\']*\'|\S+', segment)


def strip_quotes(tok: str) -> str:
    if len(tok) >= 2 and ((tok[0] == '"' and tok[-1] == '"') or (tok[0] == "'" and tok[-1] == "'")):
        return tok[1:-1]
    return tok


def check_git_main_mutation(segment: str, cwd: str) -> str | None:
    tokens = [strip_quotes(t) for t in tokenize(segment)]
    if not tokens:
        return None
    idx = 0
    if tokens[0].lower() in ("&", "call"):
        idx += 1
    if idx >= len(tokens) or os.path.basename(tokens[idx]).lower() not in ("git", "git.exe"):
        return None
    idx += 1
    while idx < len(tokens) and tokens[idx].startswith("-"):
        if tokens[idx] in ("-C", "-c", "--git-dir", "--work-tree"):
            idx += 2
        else:
            idx += 1
    if idx >= len(tokens):
        return None

    subcmd = tokens[idx].lower()
    args = tokens[idx + 1 :]
    args_before_dd = []
    for a in args:
        if a == "--":
            break
        args_before_dd.append(a)

    if subcmd == "push":
        for a in args:
            if a in ("main", "refs/heads/main") or a.endswith(":main") or a.endswith(":refs/heads/main"):
                return "Blocked: pushing to or modifying 'main' is forbidden. Target 'Remaster' / 'origin/Remaster' only."

    if subcmd in ("checkout", "switch"):
        for a in args_before_dd:
            if a in ("main", "refs/heads/main"):
                return "Blocked: switching to or checking out 'main' is forbidden. Stay on 'Remaster'."

    if subcmd == "branch":
        has_mutating_flag = any(
            a in ("-f", "--force", "-D", "-d", "--delete", "-m", "-M", "--move", "-c", "-C", "--copy")
            for a in args_before_dd
        )
        non_flag = [a for a in args_before_dd if not a.startswith("-")]
        if ("main" in non_flag or "refs/heads/main" in non_flag) and (has_mutating_flag or len(non_flag) >= 1):
            return "Blocked: modifying or recreating branch 'main' is forbidden."

    if subcmd in ("reset", "merge", "rebase", "commit", "cherry-pick", "pull"):
        if current_git_branch(cwd) == "main":
            return f"Blocked: 'git {subcmd}' cannot run while HEAD is on 'main'."

    return None


def check_lifetwin_write(cmd: str) -> str | None:
    lowered = cmd.lower()
    if "lifetwin" not in lowered:
        return None

    write_patterns = [
        r"(?:>>?|\bOut-File\b|\bSet-Content\b|\bAdd-Content\b|\bTee-Object\b)",
        r"\b(?:Copy-Item|Move-Item|Remove-Item|New-Item|Clear-Content|Rename-Item|cp|mv|rm|del|mkdir|rmdir|xcopy|robocopy)\b",
    ]
    for pat in write_patterns:
        if re.search(pat, cmd, re.IGNORECASE):
            return "Blocked: writing, copying, moving, or deleting inside C:\\Users\\karti\\LifeTwin is forbidden."

    readonly_prefixes = (
        "git grep",
        "git log",
        "git status",
        "rg ",
        "select-string",
        "get-content",
        "get-childitem",
        "test-path",
        "dir ",
        "ls ",
    )
    for seg in split_segments(cmd):
        if "lifetwin" not in seg.lower():
            continue
        seg_low = seg.lower().lstrip("& ").strip()
        if not seg_low.startswith(readonly_prefixes):
            return "Blocked: accessing LifeTwin outside strictly read-only search/inspect commands is not allowed."
    return None


def main() -> int:
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
    except Exception:
        allow()
        return 0

    tool_call = payload.get("toolCall") or {}
    args = tool_call.get("args") or {}
    cmd = args.get("CommandLine") or args.get("commandLine") or ""
    cwd = payload.get("cwd") or ""

    if not isinstance(cmd, str) or not cmd.strip():
        allow()
        return 0

    lifetwin_err = check_lifetwin_write(cmd)
    if lifetwin_err:
        deny(lifetwin_err)
        return 0

    for seg in split_segments(cmd):
        git_err = check_git_main_mutation(seg, cwd)
        if git_err:
            deny(git_err)
            return 0

    allow()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
