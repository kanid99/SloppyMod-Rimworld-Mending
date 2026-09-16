import base64, json, os, subprocess

KEY = open("../secrets/gemini_key2.txt").read().strip()
URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent"

# Written against what VFE Factory's own sprites actually do, measured: near-neutral greys,
# TRUE BLACK outlines, highlights up to ~200, and strong light-to-dark modelling across the body.
STYLE = (
    "A single 2D top-down machine sprite for a colony-simulation game, in the art style of "
    "RimWorld's Vanilla Furniture Expanded - Factory mod.\n\n"
    "STYLE, strictly:\n"
    "- TRUE overhead view looking straight down. NOT isometric, NOT angled, NOT perspective.\n"
    "- STRONG light-to-dark modelling: raised parts are clearly lighter on top, recesses are "
    "clearly darker, and there are small bright highlight edges. The machine must read as having "
    "real THICKNESS and volume, not as a flat plate.\n"
    "- Near-neutral industrial GREY throughout: dark gunmetal, mid grey, light grey.\n"
    "- TRUE BLACK outlines around the machine's silhouette and around each major part.\n"
    "- Exactly ONE saturated accent colour: CYAN, used only on the central working surface.\n"
    "- NO rust, NO grime, NO text, NO numbers, NO logos, NO arrows, NO warning stripes.\n\n"
    "OUTPUT, strictly:\n"
    "- Background: uniform PURE MAGENTA (255,0,255), flat, no shadow or glow on it.\n"
    "- The machine is centred with a margin of magenta on all four sides.\n"
    "- The machine is WIDE: roughly twice as wide as it is tall, filling the frame's width.\n"
    "- Keep the magenta margin thin - the machine should occupy most of the image.\n\n"
)

VARIANTS = {
"repair_a": (
    "SUBJECT: an automated repair machine. Its shape, left to right:\n"
    "- A tall RAISED HOUSING block at the far left and another at the far right, each clearly "
    "lifted above the rest with a lighter top face and a dark shadow along its inner edge.\n"
    "- Between them, a wide RECESSED BAY sunk into the machine: a dark rim runs around it and the "
    "floor of the bay is a flat CYAN panel.\n"
    "- Two simple angular robotic arms reach over the bay from the housings.\n"
    "- Two straight PIPE RUNS along the front and back edges connecting the two housings.\n"),

"repair_b": (
    "SUBJECT: an automated repair machine. Its shape:\n"
    "- One large RAISED HOUSING across the entire back edge, lighter on top, casting a dark band "
    "in front of it.\n"
    "- In front of that, a long RECESSED CHANNEL with a dark rim and a CYAN floor, running the "
    "full width like a conveyor trough.\n"
    "- Three squat cylindrical DRUMS standing on the machine, one at each end and one centre-back, "
    "each with a bright highlight arc on top.\n"
    "- Thick DUCTING running from the drums down into the channel.\n"),

"repair_c": (
    "SUBJECT: an automated repair machine. Its shape:\n"
    "- A heavy rectangular chassis with deeply CHAMFERED corners, the chamfers catching light.\n"
    "- A central circular RECESSED WELL with a stepped dark rim and a CYAN disc at the bottom.\n"
    "- Four blocky MOTOR HOUSINGS, one at each corner, raised well above the chassis with bright "
    "top faces and dark sides.\n"
    "- Pipe runs linking each motor housing to the central well.\n"),

"repair_d": (
    "SUBJECT: an automated repair machine. Its shape:\n"
    "- A low dark base plate, and standing on it a tall central GANTRY BRIDGE spanning the full "
    "width, brightly lit on its upper surface with heavy dark shadow beneath it.\n"
    "- Under the gantry, a RECESSED WORK BED with a dark rim and a flat CYAN surface.\n"
    "- A boxy CONTROL CABINET at the left end and a stack of three HEAT FINS at the right end, "
    "both clearly raised.\n"
    "- Short pipe stubs along the front edge.\n"),
}

def gen(name, prompt, ar="16:9", retries=3):
    payload = {"contents":[{"parts":[{"text":prompt}]}],
               "generationConfig":{"imageConfig":{"aspectRatio":ar}}}
    rp=f"out/{name}.req.json"; json.dump(payload, open(rp,"w"))
    for _ in range(retries):
        r = subprocess.run(["curl","-sS","-X","POST","-H",f"x-goog-api-key: {KEY}",
                            "-H","Content-Type: application/json","-d",f"@{rp}",URL],
                           capture_output=True, text=True, timeout=240)
        try: dd=json.loads(r.stdout)
        except Exception: print(f"  [{name}] unparseable"); continue
        if "error" in dd:
            print(f"  [{name}] {dd['error'].get('message','')[:120]}"); continue
        for p in dd["candidates"][0]["content"]["parts"]:
            if "inlineData" in p:
                open(f"out/{name}.png","wb").write(base64.b64decode(p["inlineData"]["data"]))
                print(f"  [{name}] OK"); return True
    print(f"  [{name}] FAILED"); return False

os.makedirs("out", exist_ok=True)
for n, subject in VARIANTS.items():
    gen(n, STYLE + subject)
