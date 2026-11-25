# Realmz Art Pipeline - 64 px/m Cartography Rules

Scope: `C:\Users\karti\realmz\remaster\art-pipeline\`

## 1. Visual & Technical Standards
- **Scale & Texel Lock:** Strict `64 px/m` (`64 PPU`) across terrain tiles (`128x128`), props, trees, buildings, and `4f` animation strips (`512x128` = `4 x 128x128`).
- **Color Harmony:** Quantize/grade sprites against `palette_soc_sunlit_128.json` (`Sunlit-128`).
- **Normal Maps:** Generate paired `*_n.png` maps in OpenGL `+Y` tangent-space orientation.
- **Zero Alpha Holes or Halo Bleed:** Verify solid interior alpha on roofs/foliage/props and clean 1px edge cleanup with zero magenta bleed before promoting assets.

## 2. Scratch Staging & Subprocess Hygiene
- Stage ALL intermediate renders, ComfyUI outputs, contact sheets, and QA JSON reports inside `C:\Users\karti\realmz\.scratch\` (never write to `C:\Users\karti\LifeTwin`).
- Promote only QA-passed PNG + `*_n.png` pairs to `remaster/RealmzUnity/Assets/Art/Cartography/`.
- On Windows Python scripts, pass `creationflags=0x08000000` (`CREATE_NO_WINDOW`) to `subprocess` calls.
- After promoting art into Unity, verify with `.agents/skills/realmz-visual-qa/SKILL.md` and `CartographyEditorTests`.
