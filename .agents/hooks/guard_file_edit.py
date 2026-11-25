import json
import os
import re
import sys

EM_DASH = "\u2014"
EN_DASH = "\u2013"

MONOBEHAVIOUR_CLASS_RE = re.compile(
    r"\b(?:public|internal|private|protected)?\s*(?:sealed|abstract|partial)?\s*"
    r"class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]+>)?\s*:[^{]*\bMonoBehaviour\b"
)


def allow() -> None:
    print(json.dumps({"decision": "allow"}))


def deny(reason: str) -> None:
    print(json.dumps({"decision": "deny", "reason": reason}))


def strip_cs_comments_and_strings(text: str) -> str:
    # Replace comments and string/char literals with spaces so brace counting stays accurate.
    pattern = re.compile(
        r'//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
        flags=re.DOTALL,
    )
    return pattern.sub(lambda m: "\n" * m.group(0).count("\n"), text)


def find_top_level_monobehaviours(cs_text: str) -> list[str]:
    cleaned = strip_cs_comments_and_strings(cs_text)
    has_block_ns = bool(re.search(r"\bnamespace\s+[A-Za-z0-9_.]+\s*\{", cleaned))
    top_depth = 1 if has_block_ns else 0
    results: list[str] = []
    depth = 0
    i = 0
    n = len(cleaned)
    while i < n:
        ch = cleaned[i]
        if ch == "{":
            depth += 1
            i += 1
            continue
        if ch == "}":
            depth = max(0, depth - 1)
            i += 1
            continue
        if depth <= top_depth:
            m = MONOBEHAVIOUR_CLASS_RE.match(cleaned, i)
            if m:
                results.append(m.group(1))
                i = m.end()
                continue
        i += 1
    return results


def main() -> int:
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
    except Exception:
        allow()
        return 0

    tool_call = payload.get("toolCall") or {}
    tool_name = (tool_call.get("name") or "").lower()
    args = tool_call.get("args") or {}

    path = args.get("Path") or args.get("path") or ""
    norm_path = path.replace("\\", "/").lower()

    if "/lifetwin/" in norm_path or norm_path.endswith("/lifetwin") or "c:/users/karti/lifetwin" in norm_path:
        deny("Blocked: writing or editing files inside C:\\Users\\karti\\LifeTwin is forbidden.")
        return 0

    content = args.get("Content") or ""
    replacement = args.get("Replacement") or ""
    is_append = bool(args.get("Append"))

    for label, text in (("Content", content), ("Replacement", replacement)):
        if isinstance(text, str) and (EM_DASH in text or EN_DASH in text):
            deny(
                f"Blocked: {label} contains an em-dash or en-dash (U+2014/U+2013). "
                "Use ASCII '-' or ' - ' only."
            )
            return 0

    combined_text = "\n".join(t for t in (content, replacement) if isinstance(t, str))

    # Pure RealmzCore boundary: zero UnityEngine, UnityEditor, or external NuGet refs
    if "/remaster/realmzcore/" in norm_path:
        forbidden_core = (
            "using UnityEngine",
            "using UnityEditor",
            "UnityEngine.",
            "UnityEditor.",
        )
        for token in forbidden_core:
            if token in combined_text:
                deny(f"Blocked: RealmzCore must stay pure netstandard2.1 with zero '{token}' dependencies.")
                return 0
        if norm_path.endswith(".csproj") and "<PackageReference" in combined_text:
            deny("Blocked: RealmzCore.csproj must maintain zero external NuGet PackageReference entries.")
            return 0

    # Standalone Player Safety: 1 MonoBehaviour per .cs file in RealmzUnity/Assets/Scripts/
    if "/remaster/realmzunity/assets/scripts/" in norm_path and norm_path.endswith(".cs"):
        if tool_name == "write_file" and isinstance(content, str) and content.strip() and not is_append:
            classes = find_top_level_monobehaviours(content)
            if len(classes) > 1:
                deny(
                    "Blocked: Standalone Player Safety requires strictly 1 top-level MonoBehaviour per .cs file "
                    f"(found {len(classes)}: {', '.join(classes)})."
                )
                return 0
            if len(classes) == 1:
                filename_prefix = os.path.basename(path).split(".")[0]
                if classes[0] != filename_prefix:
                    deny(
                        f"Blocked: MonoBehaviour class '{classes[0]}' must match filename prefix "
                        f"'{filename_prefix}' (e.g. {classes[0]}.cs)."
                    )
                    return 0
        elif isinstance(replacement, str) and replacement.strip():
            cleaned = strip_cs_comments_and_strings(replacement)
            classes = MONOBEHAVIOUR_CLASS_RE.findall(cleaned)
            if len(classes) > 1:
                deny(
                    "Blocked: Replacement introduces multiple MonoBehaviours in one snippet "
                    f"({', '.join(classes)})."
                )
                return 0

    allow()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
