import json, os
from pathlib import Path
import requests

GITHUB_REPO = "MATTRAX64/UltimateMenu"
GAMEBANANA_ID = "716098"
NEXUS_MOD_ID = "1330"
OUTPUT_FILE = Path(__file__).parent / "downloads.json"


def github_downloads():
    r = requests.get(f"https://api.github.com/repos/{GITHUB_REPO}/releases", headers={"Accept": "application/vnd.github+json"}, timeout=30)
    r.raise_for_status()
    return sum(a.get("download_count", 0) for rel in r.json() for a in rel.get("assets", []))


def gamebanana_downloads():
    r = requests.get("https://api.gamebanana.com/Core/Item/Data", params={"itemtype": "Mod", "itemid": GAMEBANANA_ID, "fields": "downloads", "return_keys": "true", "format": "json"}, timeout=30)
    r.raise_for_status()
    d = r.json()
    if isinstance(d, list) and d:
        d = d[0]
    if isinstance(d, dict) and d.get("downloads") is not None:
        return int(d["downloads"])
    raise RuntimeError(f"Impossible de récupérer les téléchargements GameBanana pour le mod {GAMEBANANA_ID}.")


def nexus_downloads():
    key = os.environ.get("NEXUS_API_KEY")
    if not key:
        raise RuntimeError("NEXUS_API_KEY est manquant dans GitHub Secrets.")
    r = requests.get(f"https://api.nexusmods.com/v1/games/gorillatag/mods/{NEXUS_MOD_ID}.json", headers={"apikey": key, "accept": "application/json", "Application-Name": "UltimateMenu-DownloadCounter", "Application-Version": "1.0"}, timeout=30)
    r.raise_for_status()
    return int(r.json().get("mod_downloads", 0))


def format_total(n):
    return f"{n}+" if n < 1000 else f"{n // 100 / 10:.1f}k+".replace(".", ",")


def main():
    github, gamebanana, nexus = github_downloads(), gamebanana_downloads(), nexus_downloads()
    total = github + gamebanana + nexus
    downloads = {"github": github, "gamebanana": gamebanana, "nexus": nexus, "total": total, "total_affiche": format_total(total)}
    print(json.dumps(downloads, indent=2))
    OUTPUT_FILE.write_text(json.dumps(downloads, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
