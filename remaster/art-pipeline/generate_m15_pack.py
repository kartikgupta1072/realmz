"""
Realmz Milestone 1.5 Pass A - Vibrant Sunlit Hi-Bit Asset Pack Generator.

Writes strictly inside scratch/realmz_m15_art/:
  - 6 Party Heroes (heroes/<name>.png 128x128 + <name>_n.png via laigter)
  - 6 Party Portraits (portraits/<name>_portrait.png 256x256)
  - 6 Starter Bywater Monsters (monsters/<name>.png 128x128 + <name>_n.png; Giant Frog ControlNet Canny locked)
  - 12 Multi-Variant Seamless Terrain/Dungeon Tiles (tiles/<name>.png 128x128 + periodic laigter <name>_n.png)
  - 7 2.5D Environment Props (props/<name>.png 256x256 or 128x128 + <name>_n.png via laigter)
  - Automated QA report (m15_art_qa_report.json) + zoomed labeled contact sheet (m15_art_contact_sheet.png)
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw, ImageFont

SCRATCH_ROOT = Path(r"C:\Users\karti\realmz\.scratch")
PIPE_DIR = SCRATCH_ROOT / "realmz_pixel_pipeline"
OUT_ROOT = SCRATCH_ROOT / "realmz_m15_art"
REALMZ_FROG_REF = Path(r"C:\Users\karti\realmz\remaster\extracted\monster_giant_frog_392.png")

sys.path.insert(0, str(PIPE_DIR))
sys.path.insert(0, str(OUT_ROOT))
import postprocess_m15 as pp  # noqa: E402
import run_pipeline as rp  # noqa: E402

SERVER_URL = "http://127.0.0.1:8188"
SPRITE_WORKFLOW_PATH = PIPE_DIR / "comfy_workflow_sprite_api.json"
CN_WORKFLOW_PATH = PIPE_DIR / "comfy_workflow_silhouette_cn_api.json"
PALETTE_PATH = OUT_ROOT / "palette_soc_sunlit_128.json"

SPRITE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "multiple characters, sprite sheet, grid, ground, floor, cast shadow, drop shadow, "
    "circle background, circular plate, dark circle, medallion, badge, halo, dark halo, sun halo, "
    "glowing orb behind head, aura, vignette, pedestal, platform, ground disc, shadow disc, "
    "isometric block, diorama, floating island, cube base, dirt block, grass block, terrain slab, "
    "UI, stat box, text box, red box, menu, inventory, tooltip, icon grid, floating weapon, "
    "background scenery, dark background, black background, night sky, dungeon wall, fire particles, "
    "floating embers, frame, border, text, watermark, signature, cropped weapon, cut off feet, lowres, worst quality"
)

PORTRAIT_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "full body, wide shot, multiple characters, 2boys, 2girls, duo, twin, two people, pair, "
    "symmetrical front view, rainbow stripes, neon stripes, pitch black background, pure black face, "
    "crushed shadows, text, watermark, signature, bright white background, magenta background, "
    "frame border, lowres, worst quality"
)

TILE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, perspective, horizon, sky, isometric diamond, "
    "diagonal stripes, diagonal grid, horizontal bands, stripes, striped pattern, corrugated, "
    "miniature map, world map, village, houses, trees, road edges, shoreline, whirlpool, vortex, "
    "spiral, human, person, feet, legs, hands, character, sprite, border, frame, vignette, "
    "pink bricks, red bricks, pitch black, muddy dark, watermark, text, shadow cast from outside, "
    "lowres, worst quality"
)

# ---------------------------------------------------------------------------
# Asset Definitions (37 assets -> 68 PNG files)
# ---------------------------------------------------------------------------

HEROES = [
    {
        "name": "knight",
        "label": "Knight",
        "size": 128,
        "seed": 20265101,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy armored knight hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, gleaming polished bright silver steel plate armor with sunlit "
            "gold trim, bright vibrant scarlet crimson flowing cloak, royal cobalt and gold kite shield, "
            "shining steel broadsword held upright, crested silver knight helmet, crisp dark slate ink "
            "outlines, bright warm golden-hour sunlight from upper left, soft cool slate-indigo shadows, "
            "Songs of Conquest vibrant sunlit style, no floor, no cast shadow, no circle, no pedestal"
        ),
    },
    {
        "name": "cleric",
        "label": "Cleric",
        "size": 128,
        "seed": 20266102,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated cutout game sprite, "
            "vibrant sunlit fantasy armored battle-priest cleric hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, gleaming polished silver plate pauldrons and mail over bright "
            "crimson and sunlit ivory-gold tabard vestments, gold-trimmed hood, holding heavy silver-and-gold "
            "flanged battle mace in right hand and white-and-gold kite shield in left hand, "
            "crisp dark slate ink outlines, bright warm golden-hour sunlight from upper left, "
            "Songs of Conquest sunlit style, clean empty white background, no halo, no glow behind head, no floor"
        ),
    },
    {
        "name": "mage",
        "label": "Mage",
        "size": 128,
        "seed": 20265103,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy arcane mage hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, rich royal violet-plum and vibrant cobalt-blue layered wizard robes "
            "with gleaming sunlit gold embroidery and sash, pointed wizard hood, silver beard, holding tall "
            "warm carved oak staff topped with glowing bright cerulean-aqua crystal orb, brown leather "
            "spellbook at belt, crisp dark slate ink outlines, bright warm golden-hour sunlight, "
            "Songs of Conquest sunlit style, no floor, no cast shadow, no circle, no aura"
        ),
    },
    {
        "name": "ranger",
        "label": "Ranger",
        "size": 128,
        "seed": 20265104,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy woodland ranger archer hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, bright vibrant emerald-green hooded cloak and leaf-green tunic over "
            "warm sunlit chestnut-tan leather armor with brass buckles, holding curved warm ash wooden "
            "longbow with nocked silver-tipped arrow, quiver of feathered arrows on back, warm brown "
            "leather boots, crisp dark slate ink outlines, bright warm golden-hour sunlight from upper left, "
            "Songs of Conquest sunlit style, no floor, no cast shadow, no circle"
        ),
    },
    {
        "name": "paladin",
        "label": "Paladin",
        "size": 128,
        "seed": 20265105,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid flat bright lime green background, green screen, isolated game sprite, "
            "vibrant sunlit fantasy noble paladin champion hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, gleaming bright polished silver-steel plate armor with rich sunlit "
            "gold pauldrons and trim, vibrant royal sapphire-blue and ivory surcoat, winged golden plumed "
            "helm, holding bright gold-rimmed sapphire kite shield with sun crest and gleaming broadsword, "
            "crisp dark slate ink outlines, bright golden-hour sunlight, soft cool slate-indigo shadows, "
            "Songs of Conquest sunlit style, no floor, no cast shadow, no circle"
        ),
    },
    {
        "name": "rogue",
        "label": "Rogue",
        "size": 128,
        "seed": 20266106,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated cutout game sprite, "
            "vibrant sunlit fantasy rogue swashbuckler scout hero sprite, full body head to boots centered, "
            "3/4 tactical RPG sprite, dynamic agile combat stance, bright warm tan and chestnut leather "
            "armored jerkin over bright teal-blue tunic shirt, vibrant scarlet-crimson hooded cape and scarf, "
            "brass belt buckles, holding two gleaming polished silver curved daggers firmly in both hands, "
            "warm brown leather boots, bright high-contrast colors, crisp dark slate ink outlines, "
            "bright warm golden-hour sunlight from upper left, Songs of Conquest sunlit style, no floor, no floating items"
        ),
    },
]

PORTRAITS = [
    {
        "name": "knight",
        "label": "Knight Portrait",
        "seed": 20265201,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders facing 3/4 right, "
            "valiant medieval knight wearing gleaming polished silver steel winged open-visor helm with "
            "warm sunlit gold trim, determined face clearly lit, vibrant scarlet crimson cloak over "
            "polished steel pauldrons, bright warm golden-hour sunlight from upper left, cool slate-blue "
            "castle twilight sky background, Songs of Conquest detailed hi-bit portrait style"
        ),
    },
    {
        "name": "cleric",
        "label": "Cleric Portrait",
        "seed": 20266202,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders turned 3/4 facing right, "
            "valiant bearded medieval battle-priest cleric wearing gleaming silver-steel coif and crimson "
            "gold-trimmed mantle over polished silver pauldrons, warm compassionate determined eyes, "
            "golden sun brooch clasp, bright golden-hour sunlight from upper left, soft twilight cobalt-blue "
            "cathedral sky background, Songs of Conquest detailed hi-bit portrait style"
        ),
    },
    {
        "name": "mage",
        "label": "Mage Portrait",
        "seed": 20265203,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders facing 3/4 right, "
            "wise arcane scholar mage in rich royal violet-plum hood with bright gold embroidery, "
            "flowing silver-white beard, bright cerulean-aqua arcane glow from crystal reflecting on face "
            "with warm golden rim light, deep cerulean-indigo sky background, Songs of Conquest detailed portrait"
        ),
    },
    {
        "name": "ranger",
        "label": "Ranger Portrait",
        "seed": 20265204,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1girl, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders facing 3/4 right, "
            "sharp-eyed woodland ranger archer wearing vibrant emerald-green hooded cloak and warm tan "
            "leather shoulder strap with brass clasp, sunlit warm golden-hour light across face, soft sunlit "
            "emerald forest twilight background, Songs of Conquest detailed hi-bit portrait style"
        ),
    },
    {
        "name": "paladin",
        "label": "Paladin Portrait",
        "seed": 20265205,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders facing 3/4 right, "
            "heroic noble paladin champion wearing gleaming silver-and-gold winged open-faced helm, "
            "bright sunlit gold pauldrons and royal cobalt mantle, radiant warm golden-hour sunbeams "
            "gleaming across polished armor and strong face, rich warm slate-blue sky background, "
            "Songs of Conquest detailed hi-bit portrait style"
        ),
    },
    {
        "name": "rogue",
        "label": "Rogue Portrait",
        "seed": 20265206,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, single character centered, "
            "sunlit fantasy RPG hero portrait bust close-up head and shoulders facing 3/4 right, "
            "cunning charming rogue scout wearing medium slate-blue hood, vibrant crimson lower face "
            "bandana scarf, warm chestnut leather collar with brass studs, sharp bright amber eyes lit "
            "by warm golden-hour sunlight, rich slate-indigo tavern dusk background, Songs of Conquest portrait"
        ),
    },
]

MONSTERS = [
    {
        "name": "giant_frog",
        "label": "Giant Frog [CN]",
        "size": 128,
        "use_controlnet": True,
        "cn_ref": REALMZ_FROG_REF,
        "cn_strength": 0.72,
        "denoise": 0.76,
        "seed": 20265301,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo creature, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy giant swamp frog monster sprite facing left, crouching low on four "
            "webbed terracotta-orange feet, vivid emerald-green and warm golden-ochre spotted amphibious "
            "hide, bright glowing amber eyes, wide mouth, strong muscular hind legs, 3/4 tactical RPG "
            "monster sprite, crisp dark slate ink outlines, bright warm golden-hour sunlight from upper left, "
            "Songs of Conquest sunlit style, no floor, no cast shadow, no circle"
        ),
    },
    {
        "name": "goblin_raider",
        "label": "Goblin Raider",
        "size": 128,
        "seed": 20266302,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo, no humans, "
            "simple background, solid pure white background, isolated cutout game sprite, "
            "vibrant sunlit fantasy goblin warrior monster sprite, full body head to clawed feet standing "
            "centered, 3/4 tactical RPG sprite, compact hunched bright emerald-olive green goblin creature "
            "with big pointed ears, mischievous yellow eyes, tan leather armor tunic and red loincloth, "
            "holding curved steel scimitar firmly in right hand and round studded wooden buckler shield "
            "on left arm, crisp dark slate ink outlines, bright warm golden-hour sunlight, "
            "Songs of Conquest sunlit style, single cohesive body, no floating weapons, no floor, no shadow"
        ),
    },
    {
        "name": "kobold_spearman",
        "label": "Kobold Spearman",
        "size": 128,
        "seed": 20265303,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo, no humans, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy kobold spearman monster sprite, full body head to clawed feet centered, "
            "3/4 tactical RPG sprite, agile bright terracotta-orange and warm sandstone-gold scaly "
            "reptilian kobold warrior with lizard snout, ivory horns, glowing yellow eyes, scaly tail, "
            "warm brown leather harness, holding long oak spear with gleaming silver steel spearhead "
            "diagonally forward, crisp dark slate ink outlines, bright warm golden-hour sunlight, "
            "Songs of Conquest sunlit style, no floor, no shadow, no circle"
        ),
    },
    {
        "name": "giant_sewer_rat",
        "label": "Giant Sewer Rat",
        "size": 128,
        "seed": 20265304,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo creature, "
            "simple background, solid pure white background, isolated game sprite, "
            "vibrant sunlit fantasy giant rat monster sprite facing left, quadruped beast on four clawed "
            "paws, large hunched warm chestnut-brown and sunlit ochre furry rodent body, big round tan "
            "rat ears, pointed snout with ivory fangs, bright amber eyes, long curved tan rat tail, "
            "3/4 RPG monster sprite, crisp dark slate ink outlines, bright warm golden-hour sunlight, "
            "Songs of Conquest sunlit style, no floor, no cast shadow, no circle"
        ),
    },
    {
        "name": "skeleton_warrior",
        "label": "Skeleton Warrior",
        "size": 128,
        "seed": 20265305,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo, "
            "simple background, solid flat bright lime green background, green screen, isolated game sprite, "
            "vibrant sunlit fantasy undead skeleton warrior monster sprite, full body skull to bony feet "
            "centered, 3/4 tactical RPG sprite, crisp bright warm ivory-cream bone human skeleton with "
            "clearly visible grinning skull, exposed ribcage and arm bones, glowing bright aqua-cyan eyes, "
            "weathered silver steel shoulder pauldrons, tattered crimson waist sash, holding gleaming "
            "notched steel sword and round wooden iron-rimmed shield, crisp dark slate ink outlines, "
            "bright golden-hour sunlight, Songs of Conquest sunlit style, no floor, no shadow, no circle"
        ),
    },
    {
        "name": "bandit_cutthroat",
        "label": "Bandit Cutthroat",
        "size": 128,
        "seed": 20266306,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, "
            "simple background, solid pure white background, isolated cutout game sprite, "
            "vibrant sunlit fantasy human bandit cutthroat outlaw enemy sprite, full body head to boots "
            "centered, 3/4 tactical RPG sprite, medieval highwayman wearing warm russet-brown short cloak, "
            "bright scarlet-crimson bandana mask over lower face, warm tan studded leather vest with brass "
            "buckles, holding gleaming curved silver dagger in left hand and notched steel shortsword in "
            "right hand, sturdy brown boots, completely blank pure white background everywhere around body, "
            "crisp dark slate ink outlines, bright warm golden-hour sunlight, Songs of Conquest sunlit style, "
            "no UI, no menu, no stat card, no icons, no floor, no shadow, no circle"
        ),
    },
]

TILES = [
    {
        "name": "bywater_grass_a",
        "label": "Grass A (Turf)",
        "seed": 20268401,
        "extra_neg": ", border, black border, frame, tile edge, outline, grid, square border, flowers, rocks, stones, path, dirt, mud, stripes, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, seamless full-bleed texture swatch, "
            "macro close-up continuous repeating vibrant emerald-green and sunlit spring-green meadow grass "
            "lawn surface, uniform organic dense short pixel grass blades and soft turf tufts covering "
            "every edge continuously with zero borders and zero seams, bright golden-hour daylight, "
            "Songs of Conquest RPG grass ground tile"
        ),
    },
    {
        "name": "bywater_grass_b",
        "label": "Grass B (Clover/Daisies)",
        "seed": 20265402,
        "extra_neg": ", giant flowers, bouquet, vase, dirt, rocks, dark black",
        "prompt": (
            "pixel art, hi-bit pixel art, masterpiece, no humans, top-down orthographic RPG ground tile, "
            "seamless repeating flat sunlit emerald-green meadow grass texture sprinkled with small crisp "
            "green clover leaves, tiny bright white daisies and golden-yellow buttercup blossoms, "
            "uniform all-over edge to edge, bright warm golden-hour daylight, Songs of Conquest grass tile"
        ),
    },
    {
        "name": "bywater_grass_c",
        "label": "Grass C (Dappled Moss)",
        "seed": 20266403,
        "extra_neg": ", rocks, stones, dirt, mud, flowers, stripes, diagonal lines, yellow bars, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "seamless repeating flat rich emerald and sage-olive dappled meadow grass and soft velvety "
            "moss ground surface, uniform organic all-over lush green grass clusters and bright sunlit "
            "lime-moss patches filling entire square edge to edge, Songs of Conquest RPG grass tile"
        ),
    },
    {
        "name": "cobblestone_road_a",
        "label": "Cobbles A (Sunlit)",
        "seed": 20267404,
        "extra_neg": ", drain, grate, manhole, tiny gravel noise, micro dots, brick wall, diagonal, pink, red, orange, goblet, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight top-down medieval cobblestone street pavement texture, uniform all-over chunky "
            "medium-large rounded oval warm sandstone-tan and light neutral gray river cobblestones fitted "
            "snugly together edge to edge, crisp dark brown-slate mortar gaps between stones, sunlit smooth "
            "beveled stone tops, Songs of Conquest RPG town cobblestone tile"
        ),
    },
    {
        "name": "cobblestone_road_b",
        "label": "Cobbles B (Mossy)",
        "seed": 20267405,
        "extra_neg": ", drain, grate, manhole, tiny gravel noise, brick wall, diagonal rows, stripes, pink, red, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight top-down medieval town cobblestone road texture, uniform all-over chunky rounded "
            "sunlit warm gray and sandstone-beige river paving stones fitted closely edge to edge with "
            "bright emerald-green moss sprigs growing in the dark mortar seams, Songs of Conquest town tile"
        ),
    },
    {
        "name": "cobblestone_road_c",
        "label": "Cobbles C (Worn)",
        "seed": 20267406,
        "extra_neg": ", drain, grate, manhole, vase, goblet, trophy, ornament, carpet, tapestry, tiny gravel, diagonal, pink, red, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight top-down weathered medieval stone plaza pavement texture, uniform all-over "
            "chunky irregular polygonal sunlit warm sandstone and cool light-gray worn cobblestones with "
            "warm tan sandy mortar joints and small cracks edge to edge, Songs of Conquest RPG ground tile"
        ),
    },
    {
        "name": "dirt_path_a",
        "label": "Dirt Path A (Packed)",
        "seed": 20267407,
        "extra_neg": ", blank, solid color, empty, red, crimson, blood, bricks, tiles, grid, cobblestone, masonry, water, dark black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat top-down natural brown earth dirt trail ground texture, uniform all-over chunky organic "
            "packed warm ochre-brown soil clods, light sandy-tan earth patches, shaded umber soil crevices "
            "and scattered small sandstone pebbles filling entire square edge to edge, Songs of Conquest dirt tile"
        ),
    },
    {
        "name": "dirt_path_b",
        "label": "Dirt Path B (Pebbled)",
        "seed": 20265408,
        "extra_neg": ", red, crimson, blood, bricks, tiles, grid, cobblestone, masonry, dark black",
        "prompt": (
            "pixel art, hi-bit pixel art, masterpiece, no humans, top-down orthographic RPG ground tile, "
            "seamless repeating flat sunlit warm sandy-brown dirt trail texture dotted with small warm gray "
            "sandstone pebbles and occasional tiny tufts of sunlit dry sage grass edge to edge, "
            "warm golden-hour daylight, crisp Songs of Conquest wilderness path tile"
        ),
    },
    {
        "name": "dungeon_floor_a",
        "label": "Dungeon Floor A",
        "seed": 20267409,
        "extra_neg": ", isometric diamond, diagonal, wood, wooden planks, purple, runes, text, pink, red, orange, pitch black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight top-down orthographic ancient medieval dungeon flagstone floor texture, uniform "
            "all-over straight staggered rectangular cool slate-blue and weathered warm light-gray cut "
            "stone floor slabs with crisp dark slate mortar seams, chiseled bevel edges and subtle hairline "
            "cracks filling entire square edge to edge, Songs of Conquest RPG dungeon floor tile"
        ),
    },
    {
        "name": "dungeon_floor_b",
        "label": "Dungeon Floor B (Moss)",
        "seed": 20266410,
        "extra_neg": ", isometric diamond, diagonal, pink, red, orange, terracotta, pitch black, crushed dark, wooden planks",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight top-down orthographic ancient medieval dungeon flagstone floor texture, uniform "
            "all-over straight rectangular cool slate-blue and weathered gray stone floor slabs with crisp "
            "emerald-sage moss sprigs along clean mortar seams filling entire square edge to edge, "
            "Songs of Conquest RPG dungeon floor tile"
        ),
    },
    {
        "name": "dungeon_wall",
        "label": "Dungeon Wall",
        "seed": 20269411,
        "extra_neg": ", thin stripes, tiny slats, wood planks, shutters, blinds, emblem, crest, shield, circle, windows, grates, bars, doors, moss, plants, pink, red, orange, pitch black",
        "prompt": (
            "pixel art, 16-bit, hi-bit, masterpiece, no humans, scenery texture swatch, macro close-up "
            "flat straight orthographic medieval castle stone block masonry wall texture, uniform all-over "
            "staggered running-bond courses of chunky thick rectangular chiseled cool slate-blue and warm "
            "weathered gray carved stone blocks filling entire square edge to edge, bold dark slate mortar "
            "seams, crisp beveled stone block faces and subtle cracks, Songs of Conquest RPG castle wall tile"
        ),
    },
    {
        "name": "shallow_water",
        "label": "Shallow Water",
        "seed": 20265412,
        "extra_neg": ", dark navy bands, black water, whirlpool, swirl, shore, land, sand",
        "prompt": (
            "pixel art, hi-bit pixel art, masterpiece, no humans, top-down orthographic RPG water tile, "
            "seamless repeating flat bright sunlit cerulean-blue and turquoise crystal-clear shallow stream "
            "water texture, uniform all-over crisp horizontal stylized pixel wave ripples with sparkling "
            "bright sky-cyan highlights and crisp white sunlit foam crests edge to edge, "
            "Songs of Conquest vibrant sunlit water tile"
        ),
    },
]

PROPS = [
    {
        "name": "cottage_medieval",
        "label": "Medieval Cottage (256)",
        "size": 256,
        "pad": 6,
        "seed": 20265501,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo building, "
            "simple background, solid flat bright lime green background, green screen, isolated game prop, "
            "2.5D 3/4 isometric medieval fantasy village timber-framed stone cottage house sprite, "
            "sunlit warm cream plaster walls with rich oak timber cross-beams, warm cobblestone foundation, "
            "steep pitched terracotta-brown shingled gable roof with stone chimney, sturdy oak door and "
            "warm golden-amber glowing mullioned windows, crisp dark slate ink outlines, bright warm "
            "golden-hour sunlight from upper left, Songs of Conquest town building style, no ground, no grass, no shadow"
        ),
    },
    {
        "name": "tree_oak",
        "label": "Oak Tree (256)",
        "size": 256,
        "pad": 6,
        "extra_neg": ", forest, multiple trees, woods, scenery, landscape, sky, clouds, grass ground, dirt block, isometric cube",
        "seed": 20268502,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo object, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated game sprite, "
            "one single centered lush medieval oak tree game sprite with wide empty magenta margin around "
            "all four sides, full tree from gnarled warm brown bark trunk base to big full rounded leafy "
            "crown of vibrant emerald-green and sunlit spring-green oak leaf clusters, crisp dark slate ink "
            "outlines, bright warm golden-hour sunlight from upper left, Songs of Conquest RPG map tree prop, "
            "no ground, no grass patch, no cast shadow, no forest background"
        ),
    },
    {
        "name": "tree_pine",
        "label": "Pine Tree (256)",
        "size": 256,
        "pad": 6,
        "extra_neg": ", forest, multiple trees, woods, pine cones, branch closeup, scenery, landscape, sky, snow, grass ground, dirt block, isometric cube",
        "seed": 20268503,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo object, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated game sprite, "
            "one single centered tall evergreen spruce pine tree game sprite with wide empty magenta margin "
            "around all four sides, full conifer tree from narrow warm brown trunk base to pointed triangular "
            "spire top, rich layered emerald-green and sunlit sage-teal pine needle boughs, crisp dark slate "
            "ink outlines, bright warm golden-hour sunlight from upper left, Songs of Conquest RPG tree prop, "
            "no ground, no grass patch, no cast shadow, no forest background"
        ),
    },
    {
        "name": "stone_fence",
        "label": "Stone Fence (128)",
        "size": 128,
        "pad": 4,
        "extra_neg": ", building, house, tower, lawn, yard, ground, grass block, isometric cube",
        "seed": 20269914,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo object, "
            "simple background, solid pure white background, isolated RPG game prop sprite, "
            "low horizontal medieval cobblestone stone wall fence, stacked warm brown-tan and cool slate-gray "
            "rounded fieldstones and chiseled stone blocks with small emerald moss tufts on top, "
            "bold dark ink outlines, warm golden-hour sunlight, Songs of Conquest village wall prop"
        ),
    },
    {
        "name": "market_crates",
        "label": "Market Crates (128)",
        "size": 128,
        "pad": 4,
        "use_crate_cn": True,
        "cn_strength": 0.78,
        "denoise": 0.80,
        "extra_neg": ", grid, sprite sheet, icons, weapon, hammer, house, building, scenery",
        "seed": 20269955,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo object, "
            "simple background, solid pure white background, isolated RPG game prop sprite, "
            "medieval town market supply stack of two warm chestnut-oak wooden shipping crates with nailed "
            "diagonal plank braces and iron corners beside a sturdy round oak wooden barrel bound with dark "
            "iron hoops, rich wood grain, bold crisp dark slate ink outlines, bright warm golden-hour "
            "sunlight from upper left, Songs of Conquest RPG market prop, no floor, no shadow"
        ),
    },
    {
        "name": "wildflower_bush",
        "label": "Wildflower Bush (128)",
        "size": 128,
        "pad": 4,
        "lora": "pixel-art-xl-nerijs.safetensors",
        "lora_strength": 0.90,
        "extra_neg": ", cake, slice, cube, block, diamond tile, soil, dirt, pot, planter",
        "seed": 20267506,
        "prompt": (
            "pixel art, 16-bit RPG game prop sprite, masterpiece, front view single rounded leafy green "
            "wildflower shrub bush centered, lush domed bush of vibrant emerald-green and sunlit spring-green "
            "foliage leaves dotted with bright white daisies, golden-yellow buttercups, pink and violet "
            "wildflowers, crisp dark pixel outlines, bright golden-hour sunlight, Songs of Conquest bush prop, "
            "isolated on plain solid pure white background, no ground, no dirt block, no isometric slab, no pot"
        ),
    },
    {
        "name": "torch_brazier",
        "label": "Torch Brazier (128)",
        "size": 128,
        "pad": 4,
        "seed": 20265507,
        "prompt": (
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo object, "
            "simple background, solid pure white background, isolated game prop, "
            "2.5D 3/4 isometric medieval fantasy standing wrought-iron and burnished brass tripod brazier "
            "torch prop sprite, full object top flame to tripod feet centered, ornate iron fire bowl with "
            "bright roaring golden-amber and yellow-white pixel flames and warm glowing embers, "
            "crisp dark slate ink outlines, warm golden firelight on rim, cool slate-blue shadows, "
            "Songs of Conquest prop style, no floor, no cast shadow, no dark background"
        ),
    },
]


# ---------------------------------------------------------------------------
# ComfyUI Upload & Workflow Helpers
# ---------------------------------------------------------------------------


def upload_image_to_comfy(server: str, image_path: Path, remote_name: str) -> str:
    with image_path.open("rb") as f:
        files = {"image": (remote_name, f, "image/png")}
        data = {"overwrite": "true"}
        r = requests.post(f"{server}/upload/image", files=files, data=data, timeout=30)
        r.raise_for_status()
        return r.json()["name"]


def build_sprite_workflow(
    prompt: str,
    negative: str,
    seed: int,
    filename_prefix: str,
    lora_name: str = "pixel-art-style-illustrious-by-skormino.safetensors",
    lora_strength: float = 0.88,
    steps: int = 24,
    cfg: float = 6.5,
) -> dict:
    wf = json.loads(SPRITE_WORKFLOW_PATH.read_text(encoding="utf-8"))
    wf["2"]["inputs"]["lora_name"] = lora_name
    wf["2"]["inputs"]["strength_model"] = lora_strength
    wf["2"]["inputs"]["strength_clip"] = lora_strength
    wf["4"]["inputs"]["text"] = prompt
    wf["5"]["inputs"]["text"] = negative
    wf["7"]["inputs"]["seed"] = seed
    wf["7"]["inputs"]["steps"] = steps
    wf["7"]["inputs"]["cfg"] = cfg
    wf["9"]["inputs"]["filename_prefix"] = filename_prefix
    return wf


def build_cn_workflow(
    prompt: str,
    negative: str,
    seed: int,
    remote_image_name: str,
    filename_prefix: str,
    cn_strength: float = 0.72,
    denoise: float = 0.76,
    steps: int = 26,
) -> dict:
    wf = json.loads(CN_WORKFLOW_PATH.read_text(encoding="utf-8"))
    wf["4"]["inputs"]["text"] = prompt
    wf["5"]["inputs"]["text"] = negative
    wf["6"]["inputs"]["image"] = remote_image_name
    wf["10"]["inputs"]["type"] = "canny/lineart/anime_lineart/mlsd"
    wf["11"]["inputs"]["strength"] = cn_strength
    wf["13"]["inputs"]["seed"] = seed
    wf["13"]["inputs"]["steps"] = steps
    wf["13"]["inputs"]["denoise"] = denoise
    wf["15"]["inputs"]["filename_prefix"] = filename_prefix
    return wf


def build_seam_inpaint_workflow(
    prompt: str,
    negative: str,
    seed: int,
    remote_image_name: str,
    remote_mask_name: str,
    filename_prefix: str,
    lora_name: str = "pixel-art-xl-nerijs.safetensors",
    lora_strength: float = 0.82,
    steps: int = 18,
    denoise: float = 0.76,
) -> dict:
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "Illustrious-XL-v2.0.safetensors"}},
        "2": {
            "class_type": "LoraLoader",
            "inputs": {
                "model": ["1", 0],
                "clip": ["1", 1],
                "lora_name": lora_name,
                "strength_model": lora_strength,
                "strength_clip": lora_strength,
            },
        },
        "3": {"class_type": "CLIPSetLastLayer", "inputs": {"clip": ["2", 1], "stop_at_clip_layer": -2}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": negative}},
        "6": {"class_type": "LoadImage", "inputs": {"image": remote_image_name}},
        "7": {"class_type": "LoadImageMask", "inputs": {"image": remote_mask_name, "channel": "red"}},
        "8": {"class_type": "VAEEncode", "inputs": {"pixels": ["6", 0], "vae": ["1", 2]}},
        "9": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["8", 0], "mask": ["7", 0]}},
        "10": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["2", 0],
                "positive": ["4", 0],
                "negative": ["5", 0],
                "latent_image": ["9", 0],
                "seed": seed,
                "steps": steps,
                "cfg": 6.5,
                "sampler_name": "dpmpp_2m",
                "scheduler": "karras",
                "denoise": denoise,
            },
        },
        "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}},
        "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": filename_prefix}},
    }


def prepare_frog_cn_canvas(ref_path: Path, out_path: Path) -> Path:
    """Pad Realmz giant frog RGBA onto a square solid white canvas without distortion."""
    im = Image.open(ref_path).convert("RGBA")
    arr = np.array(im)
    if np.mean(arr[..., 3] < 128) < 0.05:
        white_bg = (arr[..., 0] > 240) & (arr[..., 1] > 240) & (arr[..., 2] > 240)
        arr[white_bg, 3] = 0
    fg = arr[..., 3] >= 128
    ys, xs = np.where(fg)
    crop = arr[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1]
    ch, cw = crop.shape[:2]
    side = int(max(ch, cw) * 1.32)
    canvas = np.full((side, side, 4), 255, dtype=np.uint8)
    oy = (side - ch) // 2
    ox = (side - cw) // 2
    mask = crop[..., 3] >= 128
    canvas[oy : oy + ch, ox : ox + cw][mask] = crop[mask]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(canvas[..., :3], mode="RGB").save(out_path, format="PNG")
    return out_path


def prepare_market_crates_cn_canvas(out_path: Path) -> Path:
    """Create a clean 512x512 white-bg guide silhouette of 2 stacked wooden crates + 1 iron-banded barrel."""
    canvas = np.full((512, 512, 3), 255, dtype=np.uint8)
    oak_dark = (71, 42, 26)
    oak_mid = (163, 105, 67)
    oak_hi = (220, 159, 115)
    iron = (50, 56, 69)
    ink = (17, 19, 31)
    # Back-top wooden crate
    cv2.rectangle(canvas, (150, 130), (310, 275), oak_mid, -1)
    cv2.rectangle(canvas, (150, 130), (310, 275), ink, 6)
    cv2.line(canvas, (155, 135), (305, 270), oak_dark, 10)
    cv2.line(canvas, (150, 155), (310, 155), oak_hi, 6)
    # Front-left wooden crate
    cv2.rectangle(canvas, (80, 245), (265, 420), oak_hi, -1)
    cv2.rectangle(canvas, (80, 245), (265, 420), ink, 6)
    cv2.rectangle(canvas, (98, 263), (247, 402), oak_mid, -1)
    cv2.line(canvas, (98, 263), (247, 402), oak_hi, 12)
    cv2.line(canvas, (247, 263), (98, 402), oak_dark, 10)
    # Front-right wooden barrel with iron hoops
    cv2.ellipse(canvas, (345, 335), (82, 92), 0, 0, 360, oak_mid, -1)
    cv2.ellipse(canvas, (345, 335), (82, 92), 0, 0, 360, ink, 6)
    cv2.ellipse(canvas, (345, 252), (62, 20), 0, 0, 360, oak_hi, -1)
    cv2.ellipse(canvas, (345, 252), (62, 20), 0, 0, 360, ink, 5)
    cv2.line(canvas, (268, 298), (422, 298), iron, 14)
    cv2.line(canvas, (268, 376), (422, 376), iron, 14)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(canvas, mode="RGB").save(out_path, format="PNG")
    return out_path


def make_cross_seam_mask(size: int = 1024, band_half: int = 84, feather: int = 24) -> np.ndarray:
    """Create a 1024x1024 white cross mask over center seams (x=512, y=512) with untouched outer borders."""
    mask = np.zeros((size, size), dtype=np.float32)
    c = size // 2
    mask[c - band_half : c + band_half, :] = 1.0
    mask[:, c - band_half : c + band_half] = 1.0
    mask = cv2.GaussianBlur(mask, (0, 0), sigmaX=feather, sigmaY=feather)
    mask = np.clip(mask * 1.25, 0.0, 1.0)
    border = 40
    mask[:border, :] = 0.0
    mask[-border:, :] = 0.0
    mask[:, :border] = 0.0
    mask[:, -border:] = 0.0
    return np.clip(np.round(mask * 255.0), 0, 255).astype(np.uint8)


# ---------------------------------------------------------------------------
# Full Asset Pack Generation
# ---------------------------------------------------------------------------


def generate_pack(
    only: set[str] | None = None,
    categories: set[str] | None = None,
    force: bool = False,
    seed_offset: int = 0,
    reprocess_only: bool = False,
) -> dict[str, Any]:
    raw_dir = OUT_ROOT / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)

    remote_seam_mask = None
    if not reprocess_only:
        rp.wait_for_server(SERVER_URL, timeout=60.0)
        mask_path = raw_dir / "tile_cross_seam_mask.png"
        Image.fromarray(make_cross_seam_mask(1024), mode="L").save(mask_path, format="PNG")
        remote_seam_mask = upload_image_to_comfy(SERVER_URL, mask_path, "realmz_m15_seam_mask.png")

    t_start = time.time()
    peak_vram_all = rp.query_vram_mib()
    gen_log: list[dict[str, Any]] = []

    def should_run(cat: str, name: str, out_file: Path) -> bool:
        if categories and cat not in categories:
            return False
        if only and name not in only and f"{cat}/{name}" not in only:
            return False
        if reprocess_only:
            return True
        if out_file.exists() and not force and not only:
            return False
        return True

    # 1. Party Heroes (128x128)
    for item in HEROES:
        name = item["name"]
        out_albedo = OUT_ROOT / "heroes" / f"{name}.png"
        out_normal = OUT_ROOT / "heroes" / f"{name}_n.png"
        raw_path = raw_dir / f"hero_{name}_raw.png"
        if not should_run("heroes", name, out_albedo):
            continue
        seed = item["seed"] + seed_offset
        gen_sec, peak_mib = 0.0, peak_vram_all
        if not reprocess_only:
            wf = build_sprite_workflow(
                prompt=item["prompt"],
                negative=SPRITE_NEG,
                seed=seed,
                filename_prefix=f"realmz_m15_hero_{name}",
            )
            print(f"[HERO] Generating {name} (seed={seed})...", flush=True)
            raw_bytes, _, gen_sec, peak_mib = rp.run_comfy_prompt(SERVER_URL, wf)
            peak_vram_all = max(peak_vram_all, peak_mib)
            raw_path.write_bytes(raw_bytes)
        stats = pp.process_sprite(
            input_path=raw_path,
            output_albedo=out_albedo,
            output_normal=out_normal,
            target_size=item["size"],
            pad_px=3,
            palette_path=PALETTE_PATH,
            normal_backend="laigter",
        )
        print(
            f"  -> hero/{name}: {gen_sec:.1f}s | peak={peak_mib} MiB | colors={stats['unique_palette_colors']} "
            f"| L={stats['mean_L']} | cov={stats['opaque_coverage_pct']}% | circle={stats['suspect_circle_plate']}",
            flush=True,
        )
        gen_log.append({"category": "heroes", "name": name, "seed": seed, "sec": round(gen_sec, 2), "peak_mib": peak_mib})

    # 2. Hero Portraits (256x256)
    for item in PORTRAITS:
        name = item["name"]
        out_portrait = OUT_ROOT / "portraits" / f"{name}_portrait.png"
        raw_path = raw_dir / f"portrait_{name}_raw.png"
        if not should_run("portraits", f"{name}_portrait", out_portrait):
            if not (only and name in only and categories == {"portraits"}):
                continue
        seed = item["seed"] + seed_offset
        gen_sec, peak_mib = 0.0, peak_vram_all
        if not reprocess_only:
            wf = build_sprite_workflow(
                prompt=item["prompt"],
                negative=PORTRAIT_NEG,
                seed=seed,
                filename_prefix=f"realmz_m15_portrait_{name}",
                lora_strength=0.84,
            )
            print(f"[PORTRAIT] Generating {name}_portrait (seed={seed})...", flush=True)
            raw_bytes, _, gen_sec, peak_mib = rp.run_comfy_prompt(SERVER_URL, wf)
            peak_vram_all = max(peak_vram_all, peak_mib)
            raw_path.write_bytes(raw_bytes)
        stats = pp.process_portrait(raw_path, out_portrait, palette_path=PALETTE_PATH, target_size=256)
        print(
            f"  -> portrait/{name}: {gen_sec:.1f}s | peak={peak_mib} MiB | colors={stats['unique_palette_colors']} | L={stats['mean_L']}",
            flush=True,
        )
        gen_log.append({"category": "portraits", "name": f"{name}_portrait", "seed": seed, "sec": round(gen_sec, 2), "peak_mib": peak_mib})

    # 3. Starter Bywater Monsters (128x128)
    for item in MONSTERS:
        name = item["name"]
        out_albedo = OUT_ROOT / "monsters" / f"{name}.png"
        out_normal = OUT_ROOT / "monsters" / f"{name}_n.png"
        raw_path = raw_dir / f"monster_{name}_raw.png"
        if not should_run("monsters", name, out_albedo):
            continue
        seed = item["seed"] + seed_offset
        gen_sec, peak_mib = 0.0, peak_vram_all
        if not reprocess_only:
            if item.get("use_controlnet"):
                cn_canvas = prepare_frog_cn_canvas(item["cn_ref"], raw_dir / f"{name}_cn_input.png")
                remote_cn = upload_image_to_comfy(SERVER_URL, cn_canvas, f"realmz_m15_{name}_cn.png")
                wf = build_cn_workflow(
                    prompt=item["prompt"],
                    negative=SPRITE_NEG,
                    seed=seed,
                    remote_image_name=remote_cn,
                    filename_prefix=f"realmz_m15_monster_{name}",
                    cn_strength=item.get("cn_strength", 0.72),
                    denoise=item.get("denoise", 0.76),
                )
                print(f"[MONSTER-CN] Generating {name} (seed={seed})...", flush=True)
            else:
                wf = build_sprite_workflow(
                    prompt=item["prompt"],
                    negative=SPRITE_NEG,
                    seed=seed,
                    filename_prefix=f"realmz_m15_monster_{name}",
                )
                print(f"[MONSTER] Generating {name} (seed={seed})...", flush=True)
            raw_bytes, _, gen_sec, peak_mib = rp.run_comfy_prompt(SERVER_URL, wf)
            peak_vram_all = max(peak_vram_all, peak_mib)
            raw_path.write_bytes(raw_bytes)
        stats = pp.process_sprite(
            input_path=raw_path,
            output_albedo=out_albedo,
            output_normal=out_normal,
            target_size=item["size"],
            pad_px=3,
            palette_path=PALETTE_PATH,
            normal_backend="laigter",
        )
        print(
            f"  -> monster/{name}: {gen_sec:.1f}s | peak={peak_mib} MiB | colors={stats['unique_palette_colors']} "
            f"| L={stats['mean_L']} | cov={stats['opaque_coverage_pct']}% | circle={stats['suspect_circle_plate']}",
            flush=True,
        )
        gen_log.append({"category": "monsters", "name": name, "seed": seed, "sec": round(gen_sec, 2), "peak_mib": peak_mib})

    # 4. 12 Seamless Terrain & Dungeon Tiles (128x128)
    for item in TILES:
        name = item["name"]
        out_albedo = OUT_ROOT / "tiles" / f"{name}.png"
        out_normal = OUT_ROOT / "tiles" / f"{name}_n.png"
        seamless_path = raw_dir / f"tile_{name}_seamless_1024.png"
        if not should_run("tiles", name, out_albedo):
            continue
        seed = item["seed"] + seed_offset
        tot_sec, peak_mib = 0.0, peak_vram_all
        if not reprocess_only:
            tile_neg = TILE_NEG + item.get("extra_neg", "")
            wf1 = build_sprite_workflow(
                prompt=item["prompt"],
                negative=tile_neg,
                seed=seed,
                filename_prefix=f"realmz_m15_tile_{name}_p1",
                lora_name="pixel-art-xl-nerijs.safetensors",
                lora_strength=0.82,
                steps=22,
            )
            print(f"[TILE] Pass 1 {name} (seed={seed})...", flush=True)
            raw_bytes1, _, sec1, peak1 = rp.run_comfy_prompt(SERVER_URL, wf1)
            peak_vram_all = max(peak_vram_all, peak1)
            p1_path = raw_dir / f"tile_{name}_p1.png"
            p1_path.write_bytes(raw_bytes1)

            img_p1 = np.array(Image.open(p1_path).convert("RGB"))
            # Flatten any low-frequency edge-to-edge lighting gradient so Pass 2 cross-heal has zero brightness step
            f_p1 = img_p1.astype(np.float32)
            low_freq = cv2.GaussianBlur(f_p1, (0, 0), sigmaX=160, sigmaY=160)
            mean_rgb = f_p1.mean(axis=(0, 1), keepdims=True)
            img_p1_flat = np.clip(np.round(f_p1 - (low_freq - mean_rgb) * 0.85), 0, 255).astype(np.uint8)
            rolled = np.roll(img_p1_flat, shift=(512, 512), axis=(0, 1))
            rolled_path = raw_dir / f"tile_{name}_rolled.png"
            Image.fromarray(rolled, mode="RGB").save(rolled_path, format="PNG")
            remote_rolled = upload_image_to_comfy(SERVER_URL, rolled_path, f"realmz_m15_tile_{name}_rolled.png")

            wf2 = build_seam_inpaint_workflow(
                prompt=item["prompt"],
                negative=tile_neg,
                seed=seed + 77,
                remote_image_name=remote_rolled,
                remote_mask_name=remote_seam_mask,
                filename_prefix=f"realmz_m15_tile_{name}_p2",
                lora_name="pixel-art-xl-nerijs.safetensors",
                lora_strength=0.82,
                steps=20,
                denoise=0.82,
            )
            print(f"[TILE] Pass 2 seamless heal {name}...", flush=True)
            raw_bytes2, _, sec2, peak2 = rp.run_comfy_prompt(SERVER_URL, wf2)
            peak_vram_all = max(peak_vram_all, peak2)
            p2_path = raw_dir / f"tile_{name}_p2_rolled.png"
            p2_path.write_bytes(raw_bytes2)

            inpainted = np.array(Image.open(p2_path).convert("RGB")).astype(np.float32)
            orig_rolled = rolled.astype(np.float32)
            alpha_m = (make_cross_seam_mask(1024).astype(np.float32) / 255.0)[..., None]
            blended_rolled = np.clip(np.round(inpainted * alpha_m + orig_rolled * (1.0 - alpha_m)), 0, 255).astype(np.uint8)
            seamless_1024 = np.roll(blended_rolled, shift=(-512, -512), axis=(0, 1))
            Image.fromarray(seamless_1024, mode="RGB").save(seamless_path, format="PNG")
            tot_sec = sec1 + sec2
            peak_mib = max(peak1, peak2)

        stats = pp.process_seamless_tile(
            seamless_path,
            out_albedo,
            out_normal,
            palette_path=PALETTE_PATH,
            target_size=128,
            normal_backend="laigter",
        )
        print(
            f"  -> tile/{name}: {tot_sec:.1f}s | peak={peak_mib} MiB | colors={stats['unique_palette_colors']} "
            f"| L={stats['mean_L']} | seam={stats['seam_ratio']} | n_seam={stats['normal_seam_ratio']}",
            flush=True,
        )
        gen_log.append({"category": "tiles", "name": name, "seed": seed, "sec": round(tot_sec, 2), "peak_mib": peak_mib})

    # 5. 7 2.5D Environment Props (256x256 for large cottage/trees, 128x128 for small props)
    for item in PROPS:
        name = item["name"]
        out_albedo = OUT_ROOT / "props" / f"{name}.png"
        out_normal = OUT_ROOT / "props" / f"{name}_n.png"
        raw_path = raw_dir / f"prop_{name}_raw.png"
        if not should_run("props", name, out_albedo):
            continue
        seed = item["seed"] + seed_offset
        gen_sec, peak_mib = 0.0, peak_vram_all
        if not reprocess_only:
            prop_neg = SPRITE_NEG + item.get("extra_neg", "")
            if item.get("use_crate_cn"):
                cn_canvas = prepare_market_crates_cn_canvas(raw_dir / f"{name}_cn_input.png")
                remote_cn = upload_image_to_comfy(SERVER_URL, cn_canvas, f"realmz_m15_{name}_cn.png")
                wf = build_cn_workflow(
                    prompt=item["prompt"],
                    negative=prop_neg,
                    seed=seed,
                    remote_image_name=remote_cn,
                    filename_prefix=f"realmz_m15_prop_{name}",
                    cn_strength=item.get("cn_strength", 0.78),
                    denoise=item.get("denoise", 0.80),
                )
            else:
                wf = build_sprite_workflow(
                    prompt=item["prompt"],
                    negative=prop_neg,
                    seed=seed,
                    filename_prefix=f"realmz_m15_prop_{name}",
                    lora_name=item.get("lora", "pixel-art-style-illustrious-by-skormino.safetensors"),
                    lora_strength=item.get("lora_strength", 0.86),
                )
            print(f"[PROP] Generating {name} ({item['size']}px, seed={seed})...", flush=True)
            raw_bytes, _, gen_sec, peak_mib = rp.run_comfy_prompt(SERVER_URL, wf)
            peak_vram_all = max(peak_vram_all, peak_mib)
            raw_path.write_bytes(raw_bytes)
        stats = pp.process_sprite(
            input_path=raw_path,
            output_albedo=out_albedo,
            output_normal=out_normal,
            target_size=item["size"],
            pad_px=item.get("pad", 4),
            palette_path=PALETTE_PATH,
            normal_backend="laigter",
        )
        print(
            f"  -> prop/{name}: {gen_sec:.1f}s | peak={peak_mib} MiB | size={stats['width']}x{stats['height']} "
            f"| colors={stats['unique_palette_colors']} | L={stats['mean_L']} | cov={stats['opaque_coverage_pct']}%",
            flush=True,
        )
        gen_log.append({"category": "props", "name": name, "seed": seed, "sec": round(gen_sec, 2), "peak_mib": peak_mib})

    total_sec = round(time.time() - t_start, 2)
    return {
        "total_gen_sec": total_sec,
        "peak_vram_mib": peak_vram_all,
        "vram_budget_mib": 8151,
        "items_generated": len(gen_log),
        "log": gen_log,
    }


# ---------------------------------------------------------------------------
# Automated QA Suite
# ---------------------------------------------------------------------------


def run_qa_suite(gen_meta: dict[str, Any] | None = None) -> dict[str, Any]:
    palette_rgb = pp.load_palette(PALETTE_PATH)
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    results: list[dict[str, Any]] = []
    passed = 0
    failed = 0

    def check_sprite_pair(
        cat: str,
        name: str,
        albedo_p: Path,
        normal_p: Path,
        expected_size: int,
        is_tile: bool = False,
        allow_high_circ: bool = False,
    ) -> None:
        nonlocal passed, failed
        issues: list[str] = []
        if not albedo_p.exists():
            issues.append("missing_albedo")
        if not normal_p.exists():
            issues.append("missing_normal")
        if issues:
            failed += 1
            results.append({"category": cat, "name": name, "pass": False, "issues": issues})
            return

        spr = np.array(Image.open(albedo_p).convert("RGBA"))
        nrm = np.array(Image.open(normal_p).convert("RGBA"))
        st = pp.validate_outputs(spr, nrm, palette_rgb, is_tile=is_tile)

        if (st["width"], st["height"]) != (expected_size, expected_size):
            issues.append(f"bad_size:{st['width']}x{st['height']}!=({expected_size})")
        if nrm.shape[:2] != (expected_size, expected_size):
            issues.append(f"bad_normal_size:{nrm.shape[1]}x{nrm.shape[0]}")
        if not st["alpha_is_1bit"]:
            issues.append(f"non_1bit_alpha:{st['alpha_values']}")
        if is_tile:
            if st["alpha_values"] != [255]:
                issues.append(f"tile_has_transparency:{st['alpha_values']}")
            if st["seam_ratio"] > 1.85:
                issues.append(f"tile_seam_high:{st['seam_ratio']}")
        else:
            if st["alpha_values"] != [0, 255]:
                issues.append(f"sprite_not_transparent:{st['alpha_values']}")
            if st["opaque_coverage_pct"] < 8.0 or st["opaque_coverage_pct"] > 84.0:
                issues.append(f"suspect_coverage:{st['opaque_coverage_pct']}%")
            if not allow_high_circ and st.get("suspect_circle_plate"):
                issues.append(f"suspect_circle_plate:circ={st['circularity']},fill={st['bbox_fill']}")
        if st["off_palette_colors"] != 0:
            issues.append(f"off_palette:{st['off_palette_colors']}")
        if st["unique_palette_colors"] > 128 or st["unique_palette_colors"] < 6:
            issues.append(f"color_count_out_of_range:{st['unique_palette_colors']}")
        if not (0.985 <= st["normal_unit_mag_mean"] <= 1.015):
            issues.append(f"normal_unit_mag:{st['normal_unit_mag_mean']}")
        if st["normal_min_z"] <= 0.20:
            issues.append(f"normal_min_z_low:{st['normal_min_z']}")
        if st["normal_xy_std"] < 0.025:
            issues.append(f"normal_too_flat:{st['normal_xy_std']}")
        if not st["normal_bg_neutral_ok"]:
            issues.append("normal_bg_not_neutral")
        if st["mean_L"] < 26.0:
            issues.append(f"too_dark:mean_L={st['mean_L']}")

        ok = len(issues) == 0
        if ok:
            passed += 1
        else:
            failed += 1
        results.append({"category": cat, "name": name, "pass": ok, "issues": issues, "stats": st})

    def check_portrait(name: str, portrait_p: Path) -> None:
        nonlocal passed, failed
        issues: list[str] = []
        if not portrait_p.exists():
            failed += 1
            results.append({"category": "portraits", "name": f"{name}_portrait", "pass": False, "issues": ["missing_portrait"]})
            return
        im = np.array(Image.open(portrait_p).convert("RGBA"))
        h, w = im.shape[:2]
        alpha_vals = sorted(int(v) for v in np.unique(im[..., 3]))
        used = {tuple(int(c) for c in row) for row in im.reshape(-1, 4)[:, :3]}
        off_pal = len(used - pal_set)
        lab_all = pp.rgb_to_lab_float(im[..., :3].reshape(-1, 3))
        mean_L = round(float(lab_all[:, 0].mean()), 1)
        if (w, h) != (256, 256):
            issues.append(f"bad_size:{w}x{h}")
        if alpha_vals != [255]:
            issues.append(f"portrait_alpha:{alpha_vals}")
        if off_pal != 0:
            issues.append(f"off_palette:{off_pal}")
        if len(used) > 128 or len(used) < 12:
            issues.append(f"color_count:{len(used)}")
        if mean_L < 24.0:
            issues.append(f"portrait_too_dark:mean_L={mean_L}")
        ok = len(issues) == 0
        if ok:
            passed += 1
        else:
            failed += 1
        results.append(
            {
                "category": "portraits",
                "name": f"{name}_portrait",
                "pass": ok,
                "issues": issues,
                "stats": {
                    "width": w,
                    "height": h,
                    "alpha_values": alpha_vals,
                    "alpha_is_1bit": alpha_vals in ([0, 255], [255]),
                    "unique_palette_colors": len(used),
                    "off_palette_colors": off_pal,
                    "mean_L": mean_L,
                },
            }
        )

    for item in HEROES:
        n = item["name"]
        check_sprite_pair("heroes", n, OUT_ROOT / "heroes" / f"{n}.png", OUT_ROOT / "heroes" / f"{n}_n.png", item["size"])
    for item in PORTRAITS:
        n = item["name"]
        check_portrait(n, OUT_ROOT / "portraits" / f"{n}_portrait.png")
    for item in MONSTERS:
        n = item["name"]
        check_sprite_pair("monsters", n, OUT_ROOT / "monsters" / f"{n}.png", OUT_ROOT / "monsters" / f"{n}_n.png", item["size"])
    for item in TILES:
        n = item["name"]
        check_sprite_pair("tiles", n, OUT_ROOT / "tiles" / f"{n}.png", OUT_ROOT / "tiles" / f"{n}_n.png", 128, is_tile=True)
    for item in PROPS:
        n = item["name"]
        allow_circ = n in ("wildflower_bush", "cottage_medieval", "tree_oak", "market_crates")
        check_sprite_pair(
            "props",
            n,
            OUT_ROOT / "props" / f"{n}.png",
            OUT_ROOT / "props" / f"{n}_n.png",
            item["size"],
            allow_high_circ=allow_circ,
        )

    report = {
        "total_assets": passed + failed,
        "passed": passed,
        "failed": failed,
        "all_passed": failed == 0,
        "total_png_files": len(HEROES) * 2 + len(PORTRAITS) + len(MONSTERS) * 2 + len(TILES) * 2 + len(PROPS) * 2,
        "palette_file": str(PALETTE_PATH),
        "generation": gen_meta or {},
        "results": results,
    }
    report_path = OUT_ROOT / "m15_art_qa_report.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"[QA] Passed {passed}/{passed + failed} assets (failed={failed}) -> {report_path}", flush=True)
    for r in results:
        if not r["pass"]:
            print(f"  [FAIL] {r['category']}/{r['name']}: {r['issues']}", flush=True)
    return report


# ---------------------------------------------------------------------------
# Zoomed Labeled Contact Sheet Builder
# ---------------------------------------------------------------------------


def make_sunlit_checkerboard(size: int, cell: int = 16) -> Image.Image:
    """Mid-tone slate checkerboard so both sunlit highlights and dark outlines read clearly."""
    yy, xx = np.indices((size, size))
    chk = (((yy // cell) + (xx // cell)) % 2 == 0)[..., None]
    bg = np.where(chk, np.array([56, 66, 84], dtype=np.uint8), np.array([42, 50, 66], dtype=np.uint8))
    return Image.fromarray(bg, mode="RGB")


def composite_on_checker(sprite_path: Path, cell_px: int = 256) -> Image.Image:
    im = Image.open(sprite_path).convert("RGBA")
    big = im.resize((cell_px, cell_px), Image.Resampling.NEAREST)
    bg = make_sunlit_checkerboard(cell_px, cell=16).convert("RGBA")
    return Image.alpha_composite(bg, big).convert("RGB")


def composite_on_grass(sprite_path: Path, grass_path: Path, cell_px: int = 256) -> Image.Image:
    """Preview hero/prop standing directly on sunlit Bywater grass tile to verify figure-ground pop."""
    grass = Image.open(grass_path).convert("RGB")
    tiled = Image.new("RGB", (grass.width * 2, grass.height * 2))
    tiled.paste(grass, (0, 0))
    tiled.paste(grass, (grass.width, 0))
    tiled.paste(grass, (0, grass.height))
    tiled.paste(grass, (grass.width, grass.height))
    bg = tiled.resize((cell_px, cell_px), Image.Resampling.NEAREST).convert("RGBA")
    im = Image.open(sprite_path).convert("RGBA").resize((cell_px, cell_px), Image.Resampling.NEAREST)
    return Image.alpha_composite(bg, im).convert("RGB")


def render_tile_2x2(tile_path: Path, cell_px: int = 256) -> Image.Image:
    """Render a 2x2 tiled mosaic at 1:1 (256x256) so 128x128 seam quality is immediately visible."""
    im = Image.open(tile_path).convert("RGB")
    tiled = Image.new("RGB", (im.width * 2, im.height * 2))
    tiled.paste(im, (0, 0))
    tiled.paste(im, (im.width, 0))
    tiled.paste(im, (0, im.height))
    tiled.paste(im, (im.width, im.height))
    return tiled.resize((cell_px, cell_px), Image.Resampling.NEAREST)


def build_contact_sheet() -> Path:
    try:
        font_title = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 26)
        font_sec = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 19)
        font_lbl = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 14)
        font_sm = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 11)
    except Exception:
        font_title = font_sec = font_lbl = font_sm = ImageFont.load_default()

    cell = 256
    gap = 16
    margin = 28
    lbl_h = 24
    sec_h = 34
    cols = 6

    sheet_w = margin * 2 + cols * cell + (cols - 1) * gap
    row_block = sec_h + cell + lbl_h + 16
    prop_cell = 216
    prop_gap = 14
    prop_block = sec_h + prop_cell + lbl_h + 16

    mini = 104
    mini_gap = 10
    # 31 normal maps across 3 subrows of 11 (11*104 + 10*10 = 1244 < sheet_w)
    norm_rows = 3
    norm_strip_h = sec_h + norm_rows * (mini + 22) + 18

    # Rows:
    # 1: Heroes (6)
    # 2: Portraits (6)
    # 3: Monsters (6)
    # 4: Tiles A (6: grass a/b/c, cobble a/b/c)
    # 5: Tiles B (6: dirt a/b, dungeon floor a/b, wall, water)
    # 6: Props (7 across single wide row at 216px each: 7*216 + 6*14 = 1596 <= 1616)
    # 7: In-Context Sunlit Turf Showcase (6: knight, mage, ranger, bandit, cottage, tree_oak on Bywater grass)
    # 8: All 31 Laigter Normal Maps
    sheet_h = 84 + 6 * row_block + prop_block + norm_strip_h + margin
    sheet = Image.new("RGB", (sheet_w, sheet_h), (18, 22, 34))
    draw = ImageDraw.Draw(sheet)

    draw.rectangle([0, 0, sheet_w, 68], fill=(26, 33, 52))
    draw.line([0, 68, sheet_w, 68], fill=(236, 169, 56), width=2)
    draw.text(
        (margin, 12),
        "REALMZ M1.5 PASS A - SUNLIT HI-BIT ASSET PACK (128x128 SPRITES/TILES | 256x256 PORTRAITS/PROPS | 128-COLOR PALETTE)",
        fill=(255, 243, 176),
        font=font_title,
    )
    draw.text(
        (margin, 44),
        "37 assets (68 PNGs): 1-bit alpha [0,255], 0 mixels, 0 circular halo plates | 128-color sunlit Songs-of-Conquest palette | Laigter OpenGL +Y (#8080FF) normals",
        fill=(186, 198, 218),
        font=font_sm,
    )

    y = 84

    def draw_row(title: str, panels: list[tuple[str, Image.Image]], csize: int = cell, cgap: int = gap) -> None:
        nonlocal y
        draw.text((margin, y), title, fill=(255, 201, 82), font=font_sec)
        y += sec_h
        for i, (label, img) in enumerate(panels):
            x = margin + i * (csize + cgap)
            draw.rectangle([x - 2, y - 2, x + csize + 1, y + csize + 1], outline=(80, 98, 143), width=2)
            sheet.paste(img, (x, y))
            draw.text((x + 3, y + csize + 4), label, fill=(240, 236, 228), font=font_lbl)
        y += csize + lbl_h + 16

    # Row 1: Heroes
    draw_row(
        "1. PARTY HEROES (128x128 @ 2x | 1-bit Alpha + Sunlit 128-Color Palette)",
        [(it["label"], composite_on_checker(OUT_ROOT / "heroes" / f"{it['name']}.png", cell)) for it in HEROES],
    )

    # Row 2: Portraits
    draw_row(
        "2. PARTY HERO PORTRAITS (256x256 @ 1x | Beveled Gold/Ink Frame + Sunlit 128-Color Palette)",
        [
            (it["label"], Image.open(OUT_ROOT / "portraits" / f"{it['name']}_portrait.png").convert("RGB").resize((cell, cell), Image.Resampling.NEAREST))
            for it in PORTRAITS
        ],
    )

    # Row 3: Monsters
    draw_row(
        "3. STARTER BYWATER MONSTERS (128x128 @ 2x | Giant Frog ControlNet Locked | Clean Cutouts)",
        [(it["label"], composite_on_checker(OUT_ROOT / "monsters" / f"{it['name']}.png", cell)) for it in MONSTERS],
    )

    # Row 4: Tiles 1-6
    draw_row(
        "4A. SEAMLESS OVERWORLD TILES - GRASS & COBBLESTONES (128x128 Tiled 2x2)",
        [(f"{it['label']} (2x2)", render_tile_2x2(OUT_ROOT / "tiles" / f"{it['name']}.png", cell)) for it in TILES[:6]],
    )

    # Row 5: Tiles 7-12
    draw_row(
        "4B. SEAMLESS PATH, DUNGEON & WATER TILES (128x128 Tiled 2x2)",
        [(f"{it['label']} (2x2)", render_tile_2x2(OUT_ROOT / "tiles" / f"{it['name']}.png", cell)) for it in TILES[6:]],
    )

    # Row 6: 7 Props
    draw_row(
        "5. 2.5D ENVIRONMENT PROPS (256x256 Large Architecture/Trees | 128x128 Village Props)",
        [(it["label"], composite_on_checker(OUT_ROOT / "props" / f"{it['name']}.png", prop_cell)) for it in PROPS],
        csize=prop_cell,
        cgap=prop_gap,
    )

    # Row 7: Figure-ground contrast test on Bywater Grass A
    grass_a = OUT_ROOT / "tiles" / "bywater_grass_a.png"
    showcase = [
        ("Knight on Turf", composite_on_grass(OUT_ROOT / "heroes" / "knight.png", grass_a, cell)),
        ("Ranger on Turf", composite_on_grass(OUT_ROOT / "heroes" / "ranger.png", grass_a, cell)),
        ("Rogue on Turf", composite_on_grass(OUT_ROOT / "heroes" / "rogue.png", grass_a, cell)),
        ("Bandit on Turf", composite_on_grass(OUT_ROOT / "monsters" / "bandit_cutthroat.png", grass_a, cell)),
        ("Cottage on Turf", composite_on_grass(OUT_ROOT / "props" / "cottage_medieval.png", grass_a, cell)),
        ("Oak Tree on Turf", composite_on_grass(OUT_ROOT / "props" / "tree_oak.png", grass_a, cell)),
    ]
    draw_row("6. SUNLIT FIGURE-GROUND POP VERIFICATION (SPRITES & PROPS COMPOSITED ON BYWATER GRASS A)", showcase)

    # Row 8: All 31 Laigter Normal Maps
    draw.text(
        (margin, y),
        "7. COMPLETE LAIGTER NORMAL-MAP STRIP (ALL 31 *_n.png | OpenGL +Y Up #8080FF | Periodic Tile Normals)",
        fill=(255, 201, 82),
        font=font_sec,
    )
    y += sec_h

    all_normals: list[tuple[str, Path]] = []
    for it in HEROES:
        all_normals.append((it["name"], OUT_ROOT / "heroes" / f"{it['name']}_n.png"))
    for it in MONSTERS:
        all_normals.append((it["name"], OUT_ROOT / "monsters" / f"{it['name']}_n.png"))
    for it in TILES:
        all_normals.append((it["name"], OUT_ROOT / "tiles" / f"{it['name']}_n.png"))
    for it in PROPS:
        all_normals.append((it["name"], OUT_ROOT / "props" / f"{it['name']}_n.png"))

    per_subrow = 14
    for idx, (label, npath) in enumerate(all_normals):
        sr = idx // per_subrow
        sc = idx % per_subrow
        x = margin + sc * (mini + mini_gap)
        yy = y + sr * (mini + 22)
        nimg = Image.open(npath).convert("RGB").resize((mini, mini), Image.Resampling.NEAREST)
        draw.rectangle([x - 1, yy - 1, x + mini, yy + mini], outline=(80, 98, 143), width=1)
        sheet.paste(nimg, (x, yy))
        short_lbl = (
            label.replace("bywater_grass_", "grass_")
            .replace("cobblestone_road_", "cobble_")
            .replace("dungeon_floor_", "d_floor_")
            .replace("dungeon_wall", "d_wall")
            .replace("shallow_water", "water")
            .replace("skeleton_warrior", "skel_war")
            .replace("bandit_cutthroat", "bandit")
            .replace("giant_sewer_rat", "sewer_rat")
            .replace("cottage_medieval", "cottage")
            .replace("wildflower_bush", "wildflower")
            .replace("torch_brazier", "brazier")
        )
        draw.text((x + 2, yy + mini + 3), short_lbl[:14], fill=(216, 226, 240), font=font_sm)

    out_path = OUT_ROOT / "m15_art_contact_sheet.png"
    sheet.save(out_path, format="PNG")
    print(f"[SHEET] Wrote contact sheet ({sheet.width}x{sheet.height}) -> {out_path}", flush=True)
    return out_path


def main() -> None:
    parser = argparse.ArgumentParser(description="Realmz M1.5 Pass A Hi-Bit Asset Pack Generator")
    parser.add_argument("--only", default=None, help="Comma-separated asset names to generate/reroll")
    parser.add_argument("--categories", default=None, help="Comma-separated categories: heroes,portraits,monsters,tiles,props")
    parser.add_argument("--force", action="store_true", help="Overwrite existing assets")
    parser.add_argument("--seed-offset", type=int, default=0, help="Seed offset for rerolls")
    parser.add_argument("--reprocess-only", action="store_true", help="Skip ComfyUI; rerun postprocess_m15 on existing raws")
    parser.add_argument("--qa-only", action="store_true", help="Skip generation; run QA + contact sheet only")
    args = parser.parse_args()

    only = {s.strip() for s in args.only.split(",") if s.strip()} if args.only else None
    cats = {s.strip() for s in args.categories.split(",") if s.strip()} if args.categories else None

    gen_meta: dict[str, Any] | None = None
    if not args.qa_only:
        gen_meta = generate_pack(
            only=only,
            categories=cats,
            force=args.force,
            seed_offset=args.seed_offset,
            reprocess_only=args.reprocess_only,
        )
        print(json.dumps(gen_meta, indent=2), flush=True)

    report = run_qa_suite(gen_meta=gen_meta)
    build_contact_sheet()

    if not report["all_passed"]:
        sys.exit(2)


if __name__ == "__main__":
    main()
