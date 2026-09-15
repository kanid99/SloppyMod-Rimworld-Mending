import base64, json, subprocess, os

KEY = open("../secrets/gemini_key2.txt").read().strip()
MODEL = "gemini-2.5-flash-image"
URL = f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent"

# Written to match the vanilla tailor bench's own objects (scissors, thread spools):
# soft airbrushed shading rather than cel shading, desaturated palette, thin dark
# outlines, and a true overhead view. Asking for a whole bench is what produced
# over-detailed results before - individual objects composite far more cleanly.
STYLE = (
    "A row of separate 2D video-game item sprites for a colony simulation game, drawn in the "
    "art style of RimWorld's item icons.\n\n"
    "STYLE, strictly:\n"
    "- TRUE overhead view, looking straight down on objects lying flat on a table.\n"
    "- SOFT AIRBRUSHED shading with gentle gradients - NOT flat vector, NOT cel-shaded, "
    "NOT pixel art.\n"
    "- Muted, desaturated palette: brushed steel grey, dark gunmetal, warm mid-brown wood.\n"
    "- Thin, soft DARK GREY outlines. No thick black cartoon outlines.\n"
    "- Small simple readable silhouettes. Low detail - these are tiny sprites.\n"
    "- NO rust, NO grime, NO scratches, NO text, NO labels, NO drop shadows, NO glow.\n\n"
    "LAYOUT, strictly:\n"
    "- Background: uniform PURE MAGENTA (255,0,255), completely flat.\n"
    "- The objects are arranged in ONE horizontal row, evenly spaced.\n"
    "- WIDE magenta gaps between every object. No object touches or overlaps another.\n"
    "- No shadows cast onto the magenta.\n\n"
)

SUBJECTS = {
"metaltools_a": (
    "THE OBJECTS, left to right, five of them:\n"
    "1. A blacksmith's ball-peen hammer with a warm brown wooden handle, lying flat, "
    "handle horizontal.\n"
    "2. A pair of long steel blacksmith tongs, lying flat, slightly open.\n"
    "3. A flat steel hand file with a small brown wooden handle, lying flat at an angle.\n"
    "4. A rectangular grey whetstone block, plain, lying flat.\n"
    "5. A small loose pile of short steel rivets and metal offcuts.\n"
),
"metaltools_b": (
    "THE OBJECTS, left to right, four of them:\n"
    "1. A small steel anvil seen from directly above - a plain elongated grey block, "
    "wider at one end.\n"
    "2. A pair of steel pliers lying flat, handles together.\n"
    "3. A coil of thin steel wire, seen from above as a flat ring.\n"
    "4. Three short flat steel plates stacked slightly offset, like metal patches.\n"
),
}

def gen(name, prompt, ar="21:9", retries=3):
    payload = {"contents": [{"parts": [{"text": prompt}]}],
               "generationConfig": {"imageConfig": {"aspectRatio": ar}}}
    rp = f"out/{name}.req.json"
    json.dump(payload, open(rp, "w"))
    for a in range(retries):
        r = subprocess.run(["curl", "-sS", "-X", "POST", "-H", f"x-goog-api-key: {KEY}",
                            "-H", "Content-Type: application/json", "-d", f"@{rp}", URL],
                           capture_output=True, text=True, timeout=240)
        try:
            d = json.loads(r.stdout)
        except Exception:
            print(f"  [{name}] unparseable response"); continue
        if "error" in d:
            print(f"  [{name}] error:", d["error"].get("message", "")[:160]); continue
        for p in d["candidates"][0]["content"]["parts"]:
            if "inlineData" in p:
                open(f"out/{name}.png", "wb").write(base64.b64decode(p["inlineData"]["data"]))
                print(f"  [{name}] OK"); return True
    print(f"  [{name}] FAILED"); return False

os.makedirs("out", exist_ok=True)
for name, subject in SUBJECTS.items():
    gen(name, STYLE + subject)
