"""Unit tests for realmz_local_ai.py (runs offline with mocked HTTP)."""

from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import realmz_local_ai as lai  # noqa: E402


class TestRealmzLocalAi(unittest.TestCase):
    def test_sanitize_dashes_recursive(self) -> None:
        raw = {
            "summary": "A" + "\u2014" + "B" + "\u2013" + "C",
            "defects": ["clip" + "\u2014" + "text"],
        }
        cleaned = lai.sanitize_dashes(raw)
        self.assertEqual(cleaned["summary"], "A - B-C")
        self.assertEqual(cleaned["defects"], ["clip - text"])

    def test_filter_log_lines_condenses_noisy_log(self) -> None:
        lines = [f"Info line {i}: normal step" for i in range(500)]
        lines[42] = "Assets/Scripts/Foo.cs(10,5): error CS1002: ; expected"
        lines[250] = "NullReferenceException: Object reference not set"
        lines[495] = "PlayMode Summary: total=9 passed=8 failed=1"
        excerpt, total, hard_count = lai.filter_log_lines("\n".join(lines), max_lines=60)
        self.assertEqual(total, 500)
        self.assertGreaterEqual(hard_count, 3)
        self.assertLessEqual(len(excerpt), 60)
        joined = "\n".join(excerpt)
        self.assertIn("error CS1002", joined)
        self.assertIn("NullReferenceException", joined)
        self.assertIn("failed=1", joined)

    def test_evict_ollama_models_sends_keep_alive_zero(self) -> None:
        calls: list[tuple[str, dict | None]] = []

        def fake_http(url: str, payload=None, timeout=10.0):
            calls.append((url, payload))
            if url.endswith("/api/ps"):
                return {"models": [{"name": "qwen2.5vl:3b"}, {"name": "qwen2.5:7b-instruct"}]}
            return {"done": True}

        with mock.patch.object(lai, "http_json", side_effect=fake_http):
            unloaded = lai.evict_ollama_models("http://127.0.0.1:11434")

        self.assertEqual(unloaded, ["qwen2.5vl:3b", "qwen2.5:7b-instruct"])
        gen_calls = [p for u, p in calls if u.endswith("/api/generate")]
        self.assertEqual(len(gen_calls), 2)
        for payload in gen_calls:
            self.assertEqual(payload["keep_alive"], 0)

    def test_vision_qa_sanitizes_output_and_evicts_on_batch_error(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_dir = Path(tmp)
            p1 = tmp_dir / "shot_1.png"
            p2 = tmp_dir / "shot_2.png"
            p1.write_bytes(b"png1")
            p2.write_bytes(b"png2")

            call_idx = {"n": 0}

            def fake_chat(*args, **kwargs):
                call_idx["n"] += 1
                if call_idx["n"] == 2:
                    raise RuntimeError("simulated timeout")
                return json.dumps(
                    {
                        "pass": False,
                        "severity": "P1",
                        "defects": ["Bow card clip" + "\u2014" + "ellipsis"],
                        "summary": "Minor UI" + "\u2013" + "clip",
                    }
                )

            with (
                mock.patch.object(lai, "ollama_chat", side_effect=fake_chat),
                mock.patch.object(lai, "evict_ollama_models", return_value=["qwen2.5vl:3b"]) as evict_mock,
            ):
                args = lai.build_parser().parse_args(["vision-qa", str(tmp_dir), "--mode", "proof", "--json"])
                with self.assertRaises(RuntimeError):
                    args.func(args)
                evict_mock.assert_called_once()

            # Single image path sanitizes dashes cleanly
            with (
                mock.patch.object(lai, "ollama_chat", return_value=json.dumps({"pass": True, "severity": "NONE", "defects": [], "summary": "Clean" + "\u2014" + "frame"})),
                mock.patch.object(lai, "record_stats"),
            ):
                buf = io.StringIO()
                args = lai.build_parser().parse_args(["vision-qa", str(p1), "--mode", "proof", "--json"])
                with redirect_stdout(buf):
                    rc = args.func(args)
                self.assertEqual(rc, 0)
                parsed = json.loads(buf.getvalue())
                self.assertEqual(parsed["results"][0]["summary"], "Clean - frame")

    def test_parse_args_all_subcommands(self) -> None:
        parser = lai.build_parser()
        self.assertEqual(parser.parse_args(["status", "--json"]).command, "status")
        self.assertEqual(parser.parse_args(["evict"]).command, "evict")
        self.assertEqual(parser.parse_args(["vision-qa", "img.png", "--mode", "sprite"]).mode, "sprite")
        self.assertEqual(parser.parse_args(["log-triage", "a.log", "--tier", "heavy"]).tier, "heavy")
        self.assertEqual(parser.parse_args(["summarize", "doc.md", "--focus", "combat"]).focus, "combat")
        self.assertEqual(parser.parse_args(["draft-content", "--kind", "item", "--spec", "torch"]).kind, "item")


if __name__ == "__main__":
    unittest.main()
