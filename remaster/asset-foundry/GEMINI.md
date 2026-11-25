# Realmz Asset Foundry - Tooling & Parity Rules

Scope: `C:\Users\karti\realmz\remaster\asset-foundry\`

## 1. Environment & Verification
- Run the test suite with the dedicated venv or `dev.ps1`:
  - `C:\Users\karti\realmz\tools\asset-foundry\.venv\Scripts\python.exe -m unittest discover -s C:\Users\karti\realmz\remaster\asset-foundry\tests -v`
  - Or `powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 test-python`

## 2. CLI Mirror Parity & Hygiene
- Keep `C:\Users\karti\realmz\remaster\asset-foundry\bin\realmz-foundry.py` SHA-256 identical to `C:\Users\karti\.local\bin\realmz-foundry.py`.
- Pass `CREATE_NO_WINDOW = 0x08000000` on all Windows Python `subprocess` calls (`foundry/config.py`).
- Direct all temporary workpacks, intermediate GLBs, and QA sheets into `C:\Users\karti\realmz\.scratch\`.
