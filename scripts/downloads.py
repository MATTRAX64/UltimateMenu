import json, os
from pathlib import Path
import requests

GITHUB_REPO, GAMEBANANA_ID, NEXUS_MOD_ID = "MATTRAX64/UltimateMenu", "716098", "1330"

def get(url, **kwargs):
    response = requests.get(url, timeout=30, **kwargs)
    response.raise_for_status()
    return response

def github_downloads():
    url, total = f"https://api.github.com/repos/{GITHUB_REPO}/releases?per_page=100", 0
    while url:
        response = get(url)
        total += sum(asset.get("download_count", 0) for release in response.json() for asset in release.get("assets", []))
        url = response.links.get("next", {}).get("url")
    return total

def main():
    key = os.getenv("NEXUS_API_KEY")
    if not key:
        raise RuntimeError("NEXUS_API_KEY est manquant dans GitHub Secrets.")

    params = dict(itemtype="Mod", itemid=GAMEBANANA_ID, fields="downloads", return_keys="true", format="json")
    banana = get("https://api.gamebanana.com/Core/Item/Data", params=params).json()
    banana = banana[0] if isinstance(banana, list) and banana else banana
    headers = {"apikey": key, "Accept": "application/json", "Application-Name": "UltimateMenu-DownloadCounter", "Application-Version": "1.0"}
    nexus = get(f"https://api.nexusmods.com/v1/games/gorillatag/mods/{NEXUS_MOD_ID}.json", headers=headers).json()

    github, gamebanana, nexus, thunderstore_excalibur = github_downloads(), int(banana["downloads"]), int(nexus["mod_downloads"]), 2500
    counts = {"github": github, "gamebanana": gamebanana, "nexus": nexus, "Thunderstore + Excalibur": thunderstore_excalibur}
    total = sum(counts.values())
    counts.update(total=total, total_affiche=f"{total}+" if total < 1000 else f"{total // 1000},{total // 100 % 10}k+")
    output = json.dumps(counts, indent=2)
    Path(__file__).with_name("downloads.json").write_text(output, encoding="utf-8")
    print(output)

if __name__ == "__main__":
    main()
