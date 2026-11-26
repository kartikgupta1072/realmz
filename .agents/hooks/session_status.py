import hashlib
import json
import os
import subprocess
import sys
import urllib.request

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


def run_cmd(args: list[str], cwd: str) -> str:
    try:
        res = subprocess.run(
            args,
            cwd=cwd,
            capture_output=True,
            text=True,
            timeout=4,
            creationflags=CREATE_NO_WINDOW,
        )
        if res.returncode == 0:
            return res.stdout.strip()
    except Exception:
        pass
    return ""


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(65536)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def check_core_dll_status(repo_root: str) -> str:
    release_dll = os.path.join(repo_root, "remaster", "RealmzCore", "bin", "Release", "netstandard2.1", "RealmzCore.dll")
    plugin_dll = os.path.join(repo_root, "remaster", "RealmzUnity", "Assets", "Plugins", "RealmzCore", "RealmzCore.dll")
    core_dir = os.path.join(repo_root, "remaster", "RealmzCore")

    if not os.path.isfile(plugin_dll):
        return "PLUGIN_MISSING"
    if not os.path.isfile(release_dll):
        return "RELEASE_NOT_BUILT"

    try:
        if sha256_file(release_dll) != sha256_file(plugin_dll):
            return "DLL_MISMATCH"
        plugin_mtime = os.path.getmtime(plugin_dll)
        for root, dirs, files in os.walk(core_dir):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
            for fn in files:
                if fn.endswith(".cs") or fn.endswith(".csproj"):
                    if os.path.getmtime(os.path.join(root, fn)) > plugin_mtime + 1.0:
                        return "STALE_SOURCE"
        return "IN_SYNC"
    except Exception:
        return "UNKNOWN"


def is_unity_running() -> bool:
    try:
        res = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH"],
            capture_output=True,
            text=True,
            timeout=4,
            creationflags=CREATE_NO_WINDOW,
        )
        return "unity.exe" in res.stdout.lower()
    except Exception:
        return False


def check_ollama_status() -> str:
    try:
        with urllib.request.urlopen("http://127.0.0.1:11434/api/tags", timeout=0.8) as resp:
            if resp.status == 200:
                data = json.loads(resp.read().decode("utf-8", errors="ignore"))
                models = data.get("models") or []
                return f"READY({len(models)} models)" if models else "READY"
    except Exception:
        pass
    return "OFFLINE"


def main() -> int:
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
        workspace_paths = payload.get("workspacePaths") or []
        repo_root = workspace_paths[0] if workspace_paths else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

        branch = run_cmd(["git", "rev-parse", "--abbrev-ref", "HEAD"], repo_root) or "unknown"
        head = run_cmd(["git", "rev-parse", "--short", "HEAD"], repo_root) or "unknown"
        dirty_raw = run_cmd(["git", "status", "--porcelain"], repo_root)
        dirty_count = len([ln for ln in dirty_raw.splitlines() if ln.strip()]) if dirty_raw else 0
        dll_status = check_core_dll_status(repo_root)
        unity_state = "RUNNING" if is_unity_running() else "IDLE"
        ollama_state = check_ollama_status()

        msg = (
            f"[Realmz Workspace] branch={branch} | head={head} | dirty={dirty_count} | "
            f"RealmzCore.dll={dll_status} | UnityEditor={unity_state} | Ollama={ollama_state} | "
            "Routing=2+lanes->flash_subagents;>=3PNG/logs->dev.ps1_local-ai | CLI=remaster/dev.ps1"
        )
        print(json.dumps({"injectSteps": [{"systemMessage": {"systemMessage": msg}}]}))
    except Exception:
        print("{}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
