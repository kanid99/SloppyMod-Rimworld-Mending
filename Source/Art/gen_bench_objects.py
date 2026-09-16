"""Purpose-drawn objects for a MODERN repair bench: our own sewing machine and thread spools
rather than the vanilla ones, plus the power tools the layout needs."""
import base64, json, os, subprocess
from concurrent.futures import ThreadPoolExecutor

KEY = open(os.environ.get("GEMINI_KEY_FILE", "../secrets/gemini_key2.txt")).read().strip()
URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent"

STYLE = (
    "A row of separate 2D video-game item sprites for a colony simulation game, drawn in the "
    "art style of RimWorld's item icons.\n\n"
    "STYLE, strictly:\n"
    "- TRUE overhead view, looking straight down on objects standing on a table. Seen from "
    "directly above, so a machine reads as its own footprint - never from the side, never at "
    "an angle.\n"
    "- SOFT AIRBRUSHED shading with gentle gradients - NOT flat vector, NOT cel-shaded, "
    "NOT pixel art.\n"
    "- Muted, desaturated palette: brushed steel grey, dark gunmetal, off-white plastic.\n"
    "- Thin, soft DARK GREY outlines. No thick black cartoon outlines.\n"
    "- Small simple readable silhouettes. Low detail - these are tiny sprites.\n"
    "- NO glowing screens, NO neon, NO rust, NO grime, NO text, NO labels, NO drop shadows.\n\n"
    "LAYOUT, strictly:\n"
    "- Background: uniform PURE MAGENTA (255,0,255), completely flat.\n"
    "- The objects are arranged in ONE horizontal row, evenly spaced.\n"
    "- WIDE magenta gaps between every object. No object touches or overlaps another.\n"
    "- No shadows cast onto the magenta.\n\n"
)

SHEETS = {
"modern_a": ("THE OBJECTS, left to right, three of them:\n"
  "1. A MODERN INDUSTRIAL SEWING MACHINE seen from DIRECTLY ABOVE: a squat off-white and "
  "dark-grey machine body shaped like a flattened C, with the arm reaching right and the "
  "needle head at its end over a small steel bed plate. A dark handwheel disc on the right "
  "side of the body. Compact and machine-like, not decorative.\n"
  "2. A CORDLESS POWER DRILL lying flat, seen from above: a dark grey body with a chuck at "
  "one end and a battery pack under the grip.\n"
  "3. A small BENCH ARBOR PRESS seen from directly above: a heavy dark cast base with a round "
  "steel ram in the middle and a straight lever arm reaching to one side.\n"),
"modern_b": ("THE OBJECTS, left to right, four of them:\n"
  "1. A RACK OF FOUR THREAD SPOOLS seen from directly above: four upright cylindrical spools "
  "in a row on a small dark steel stand, wound with muted thread - one dull red, one dull "
  "blue, one dull olive, one bone white. Seen from above each spool reads as a ring.\n"
  "2. A single larger CONE OF THREAD seen from above, a pale ring with a dark core.\n"
  "3. A modern steel ADJUSTABLE WRENCH lying flat.\n"
  "4. A flat DIGITAL CALLIPER lying flat, dark grey with a pale grey scale strip.\n"),
"modern_c": ("THE OBJECTS, left to right, four of them:\n"
  "1. A modern grey TOOL CADDY seen from directly above: a shallow open tray divided into "
  "compartments with the tops of a few hand tools showing.\n"
  "2. A small BENCH VICE seen from directly above, dark cast steel with a screw handle.\n"
  "3. A compact HEAT PRESS seen from directly above: a square dark plate on a short arm over "
  "a pale base plate.\n"
  "4. A coil of dark insulated CABLE with a plug end.\n"),
}

def gen(item, retries=3):
    name, subject = item
    payload={"contents":[{"parts":[{"text":STYLE+subject}]}],
             "generationConfig":{"imageConfig":{"aspectRatio":"21:9"}}}
    rp=f"out/{name}.req.json"; json.dump(payload, open(rp,"w"))
    for _ in range(retries):
        r=subprocess.run(["curl","-sS","-X","POST","-H",f"x-goog-api-key: {KEY}",
                          "-H","Content-Type: application/json","-d",f"@{rp}",URL],
                         capture_output=True, text=True, timeout=300)
        try: d=json.loads(r.stdout)
        except Exception: continue
        if "error" in d: print(f"  [{name}]", d["error"].get("message","")[:100]); continue
        for p in d["candidates"][0]["content"]["parts"]:
            if "inlineData" in p:
                open(f"out/{name}.png","wb").write(base64.b64decode(p["inlineData"]["data"]))
                print(f"  [{name}] OK"); return name
    print(f"  [{name}] FAILED"); return None

os.makedirs("out", exist_ok=True)
with ThreadPoolExecutor(max_workers=3) as ex:
    print(len([n for n in ex.map(gen, SHEETS.items()) if n]), "sheets")
