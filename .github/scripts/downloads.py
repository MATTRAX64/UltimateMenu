import json, os, re
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen

NEXT_LINK = re.compile(r'<([^>]+)>\s*;\s*rel="next"')

GITHUB_REPO, GAMEBANANA_ID, NEXUS_MOD_ID = "MATTRAX64/UltimateMenu", "716098", "1330"

def get(url, *, params=None, headers=None):
    if params:
        url += "?" + urlencode(params)
    request = Request(url, headers={"User-Agent": "UltimateMenu-DownloadCounter", **(headers or {})})
    with urlopen(request, timeout=30) as response:
        match = NEXT_LINK.search(response.headers.get("Link", ""))
        return json.load(response), match.group(1) if match else None

def github_downloads():
    url, total = f"https://api.github.com/repos/{GITHUB_REPO}/releases?per_page=100", 0
    while url:
        releases, url = get(url, headers={"Accept": "application/vnd.github+json"})
        total += sum(a["download_count"] for release in releases for a in release["assets"])
    return total

def main():
    key = os.getenv("NEXUS_API_KEY")
    if not key:
        raise RuntimeError("NEXUS_API_KEY est manquant dans GitHub Secrets.")

    params = dict(itemtype="Mod", itemid=GAMEBANANA_ID, fields="downloads", return_keys="true", format="json")
    banana = get("https://api.gamebanana.com/Core/Item/Data", params=params)[0]
    banana = banana[0] if isinstance(banana, list) and banana else banana
    headers = {"apikey": key, "Accept": "application/json", "Application-Name": "UltimateMenu-DownloadCounter", "Application-Version": "1.0"}
    nexus = get(f"https://api.nexusmods.com/v1/games/gorillatag/mods/{NEXUS_MOD_ID}.json", headers=headers)[0]

    github, gamebanana, nexus, thunderstore_excalibur = github_downloads(), int(banana["downloads"]), int(nexus["mod_downloads"]), 2500
    counts = {"github": github, "gamebanana": gamebanana, "nexus": nexus, "Thunderstore + Excalibur": thunderstore_excalibur}
    total = sum(counts.values())
    counts.update(total=total, total_affiche=f"{total}+" if total < 1000 else f"{total // 1000},{total // 100 % 10}k+")
    output = json.dumps(counts, indent=2)
    destination = Path(__file__).with_name("downloads.json")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(output + "\n", encoding="utf-8")
    print(output)

if __name__ == "__main__":
    main()
