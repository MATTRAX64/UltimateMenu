import json
import os
from pathlib import Path

import requests


GITHUB_REPO = "MATTRAX64/UltimateMenu"

# Tous les packages Thunderstore à additionner
THUNDERSTORE_PACKAGES = [
    ("MATTRAX", "UltimateMenu"),
    ("MATTRAX", "GTAG_UltimateMenu_v1_6_0"),
]

GAMEBANANA_IDS = [
    "716098",
]

# Tous les mods Nexus à additionner
NEXUS_MOD_IDS = [
    "1330",
    "1338",
]

OUTPUT_FILE = Path(__file__).parent / "downloads.json"


def github_downloads():
    """
    Additionne les téléchargements de TOUS les assets
    de TOUTES les releases GitHub.
    """
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


def thunderstore_package_downloads(namespace, package):
    response = requests.get(
        f"https://thunderstore.io/api/v1/package-metrics/{namespace}/{package}/",
        timeout=30
    )
    response.raise_for_status()

    return int(response.json().get("downloads", 0))


def thunderstore_downloads():
    """
    Additionne les téléchargements de tous les packages Thunderstore.
    """
    total = 0

    for namespace, package in THUNDERSTORE_PACKAGES:
        downloads = thunderstore_package_downloads(namespace, package)

        print(
            f"Thunderstore {namespace}/{package}: "
            f"{downloads:,} downloads"
        )

        total += downloads

    return total


def gamebanana_mod_downloads(mod_id):
    response = requests.get(
        "https://api.gamebanana.com/Core/Item/Data",
        params={
            "itemtype": "Mod",
            "itemid": mod_id,
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

    raise RuntimeError(
        f"Impossible de récupérer les téléchargements "
        f"GameBanana pour le mod {mod_id}."
    )


def gamebanana_downloads():
    total = 0

    for mod_id in GAMEBANANA_IDS:
        downloads = gamebanana_mod_downloads(mod_id)

        print(
            f"GameBanana {mod_id}: "
            f"{downloads:,} downloads"
        )

        total += downloads

    return total


def nexus_mod_downloads(mod_id, api_key):
    response = requests.get(
        f"https://api.nexusmods.com/v1/games/gorillatag/mods/{mod_id}.json",
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


def nexus_downloads():
    """
    Additionne les téléchargements de tous les mods Nexus.
    """
    api_key = os.environ.get("NEXUS_API_KEY")

    if not api_key:
        raise RuntimeError(
            "NEXUS_API_KEY est manquant dans GitHub Secrets."
        )

    total = 0

    for mod_id in NEXUS_MOD_IDS:
        downloads = nexus_mod_downloads(mod_id, api_key)

        print(
            f"Nexus {mod_id}: "
            f"{downloads:,} downloads"
        )

        total += downloads

    return total


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

    print("\n--- TOTALS ---")
    print(json.dumps(downloads, indent=2))

    OUTPUT_FILE.write_text(
        json.dumps(downloads, indent=2),
        encoding="utf-8"
    )


if __name__ == "__main__":
    main()
