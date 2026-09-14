import json
import os
from pathlib import Path

import requests


GITHUB_REPO = "MATTRAX64/UltimateMenu"
THUNDERSTORE_URL = "https://thunderstore.io/api/v1/package-metrics/MATTRAX/UltimateMenu/"
GAMEBANANA_ID = "716098"
NEXUS_MOD_ID = "1330"

OUTPUT_FILE = Path(__file__).parent / "downloads.json"


def github_downloads():
    response = requests.get(
        f"https://api.github.com/repos/{GITHUB_REPO}/releases",
        headers={"Accept": "application/vnd.github+json"},
        timeout=30
    )
    response.raise_for_status()

    return sum(
        asset.get("download_count", 0)
        for release in response.json()
        for asset in release.get("assets", [])
    )


def thunderstore_downloads():
    response = requests.get(
        THUNDERSTORE_URL,
        timeout=30
    )
    response.raise_for_status()

    return int(response.json().get("downloads", 0))


def gamebanana_downloads():
    response = requests.get(
        "https://api.gamebanana.com/Core/Item/Data",
        params={
            "itemtype": "Mod",
            "itemid": GAMEBANANA_ID,
            "fields": "downloads",
            "return_keys": "true",
            "format": "json"
        },
        timeout=30
    )
    response.raise_for_status()

    data = response.json()

    if isinstance(data, dict) and data.get("downloads") is not None:
        return int(data["downloads"])

    if isinstance(data, list) and data:
        if isinstance(data[0], dict) and data[0].get("downloads") is not None:
            return int(data[0]["downloads"])

    raise RuntimeError("Impossible de récupérer les téléchargements GameBanana.")


def nexus_downloads():
    api_key = os.environ.get("NEXUS_API_KEY")

    if not api_key:
        raise RuntimeError("NEXUS_API_KEY est manquant dans GitHub Secrets.")

    response = requests.get(
        f"https://api.nexusmods.com/v1/games/gorillatag/mods/{NEXUS_MOD_ID}.json",
        headers={
            "apikey": api_key,
            "accept": "application/json",
            "Application-Name": "UltimateMenu-DownloadCounter",
            "Application-Version": "1.0"
        },
        timeout=30
    )
    response.raise_for_status()

    return int(response.json().get("mod_downloads", 0))


def main():
    github = github_downloads()
    thunderstore = thunderstore_downloads()
    gamebanana = gamebanana_downloads()
    nexus = nexus_downloads()

    downloads = {
        "github": github,
        "thunderstore": thunderstore,
        "gamebanana": gamebanana,
        "nexus": nexus,
        "total": github + thunderstore + gamebanana + nexus
    }

    OUTPUT_FILE.write_text(
        json.dumps(downloads, indent=2),
        encoding="utf-8"
    )


if __name__ == "__main__":
    main()
