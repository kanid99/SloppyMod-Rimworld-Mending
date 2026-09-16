"""i02's shape, drawn the way VFE Factory actually draws.

Measured off their sprites rather than described from memory: flat blocked faces, thick true
black outlines, a warm grey-brown chassis against cool slate machinery, and one muted orange
accent. The earlier bodies were airbrushed and photoreal, which is why they read as foreign
next to vanilla.
"""
import base64, json, os, subprocess
from concurrent.futures import ThreadPoolExecutor

KEY = open("../secrets/gemini_key2.txt").read().strip()
URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent"

STYLE = (
    "A single 2D top-down machine sprite for RimWorld, drawn in the exact art style of the "
    "Vanilla Furniture Expanded - Factory mod.\n\n"
    "VIEW: TRUE overhead, looking straight down. NOT isometric, NOT angled, NOT perspective.\n\n"
    "HOW IT IS DRAWN - this matters more than the subject:\n"
    "- FLAT GRAPHIC SHADING. Each face is ONE flat tone, or at most a simple two-step gradient. "
    "NO airbrushing, NO soft photoreal metal, NO glossy reflections, NO specular highlights.\n"
    "- THICK TRUE BLACK OUTLINES around the whole silhouette and around every major part, with "
    "thinner black lines separating panels inside it.\n"
    "- BOLD SIMPLE SHAPES. A small number of large rectangles and cylinders. Do NOT cover the "
    "machine in small bolts, tiny screws, fine bevels or busy surface detail.\n"
    "- Where there is detail it is a ROW OF IDENTICAL PLAIN BARS - slats, fins or rollers - "
    "drawn as flat stripes, evenly spaced.\n"
    "- Raised parts are a clearly LIGHTER flat tone; recesses are a clearly DARKER flat tone. "
    "The depth comes from the tone steps and the black lines, not from soft shading.\n\n"
    "PALETTE, strictly:\n"
    "- Chassis and casings: warm neutral GREY-BROWN, light grey on top faces, dark brown-grey "
    "in the recesses.\n"
    "- Machinery and rollers: cool SLATE GREY.\n"
    "- ONE accent only: a muted ORANGE, used on a few small strips. NOT bright, NOT glowing.\n"
    "- NO cyan, NO blue, NO teal, NO green, NO purple. NOTHING emits light.\n"
    "- NO text, NO numbers, NO logos, NO hazard stripes, NO rust, NO grime.\n\n"
    "OUTPUT, strictly:\n"
    "- Background: uniform PURE MAGENTA (255,0,255), flat.\n"
    "- The machine is centred with a thin magenta margin on all four sides.\n"
    "- The machine is only SLIGHTLY WIDER than it is tall - about five wide to four tall - and fills most of the frame in BOTH directions.\n\n"
    "SYMMETRY: mirror-symmetrical about the vertical centre line; any single control block "
    "sits exactly on that centre line.\n\n"
)

BASE = (
    "SUBJECT: an automated repair machine, seen from above as a nearly square machine. Its "
    "shape, which must not change:\n"
    "- A large raised rectangular HOOD filling the middle of the machine, its top face a light "
    "flat plate with a row of plain dark LOUVRE SLOTS cut across it.\n"
    "- Below the hood's front lip, a dark recessed WORK SLOT running most of the machine's "
    "width, with a muted orange strip along its bottom edge.\n"
    "- A tall ribbed cylindrical TANK standing at each end, drawn as a plain cylinder with a "
    "few flat bands across it.\n"
    "- A row of flat panelled DECK PLATES along the back edge above the hood.\n"
    "- A small plain control block centred on the hood's front edge.\n")

TWEAK = {
 "w1": "",
 "w2": "- The hood's top plate is split into three flat panels by two black seams.\n",
 "w3": "- The tanks are wider and lower, and a straight pipe runs along the back edge between "
       "them.\n",
 "w4": "- The louvre slots run front-to-back instead of side-to-side.\n",
 "w5": "- A flat step runs along the front edge below the work slot, in a lighter tone.\n",
 "w6": "- The hood is narrower, leaving a flat panelled deck visible at each side between the "
       "hood and the tanks.\n",
}

def gen(item, retries=4):
    name, extra = item
    payload={"contents":[{"parts":[{"text":STYLE+BASE+extra}]}],
             "generationConfig":{"imageConfig":{"aspectRatio":"4:3"}}}
    rp=f"out/repair_{name}.req.json"; json.dump(payload, open(rp,"w"))
    for _ in range(retries):
        r=subprocess.run(["curl","-sS","-X","POST","-H",f"x-goog-api-key: {KEY}",
                          "-H","Content-Type: application/json","-d",f"@{rp}",URL],
                         capture_output=True, text=True, timeout=300)
        try: d=json.loads(r.stdout)
        except Exception: continue
        if "error" in d: print(f"  [{name}]", d["error"].get("message","")[:90]); continue
        for p in d["candidates"][0]["content"]["parts"]:
            if "inlineData" in p:
                open(f"out/repair_{name}.png","wb").write(base64.b64decode(p["inlineData"]["data"]))
                print(f"  [repair_{name}] OK"); return name
    print(f"  [repair_{name}] FAILED"); return None

if __name__ == '__main__':
    os.makedirs("out", exist_ok=True)
    with ThreadPoolExecutor(max_workers=3) as ex:
        print(len([n for n in ex.map(gen, TWEAK.items()) if n]), "bodies")
