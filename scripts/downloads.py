import json
import os
import requests

GITHUB_REPO = "MATTRAX64/UltimateMenu"

THUNDERSTORE_URL = (
    "https://thunderstore.io/api/v1/package-metrics/"
    "MATTRAX/UltimateMenu/"
)

GAMEBANANA_ID = "716098"
NEXUS_MOD_ID = "1330"


def github_downloads():
    url = f"https://api.github.com/repos/{GITHUB_REPO}/releases"

    response = requests.get(
        url,
        headers={"Accept": "application/vnd.github+json"},
        timeout=30
    )
    response.raise_for_status()

    total = 0

    for release in response.json():
        for asset in release.get("assets", []):
            total += asset.get("download_count", 0)

    return total


def thunderstore_downloads():
    response = requests.get(
        THUNDERSTORE_URL,
        timeout=30
    )
    response.raise_for_status()

    data = response.json()

    return int(data.get("downloads", 0))


def gamebanana_downloads():
    url = "https://api.gamebanana.com/Core/Item/Data"

    params = {
        "itemtype": "Mod",
        "itemid": GAMEBANANA_ID,
        "fields": "name,Downloads"
    }

    response = requests.get(
        url,
        params=params,
        timeout=30
    )
    response.raise_for_status()

    data = response.json()

    # GameBanana peut retourner les données sous différentes formes.
    if isinstance(data, dict):
        for key in ("Downloads", "_nDownloadCount", "download_count"):
            if key in data:
                return int(data[key])

    if isinstance(data, list):
        for value in data:
            if isinstance(value, (int, float)):
                return int(value)

    print("GameBanana: compteur non trouvé.")
    return 0


def nexus_downloads():
    api_key = os.environ.get("NEXUS_API_KEY")

    if not api_key:
        raise RuntimeError(
            "NEXUS_API_KEY est manquant dans GitHub Secrets."
        )

    url = (
        "https://api.nexusmods.com/v1/games/"
        f"gorillatag/mods/{NEXUS_MOD_ID}.json"
    )

    headers = {
        "apikey": api_key,
        "accept": "application/json",
        "Application-Name": "UltimateMenu-DownloadCounter",
        "Application-Version": "1.0",
    }

    response = requests.get(
        url,
        headers=headers,
        timeout=30
    )
    response.raise_for_status()

    data = response.json()

    return int(data.get("mod_downloads", 0))


def main():
    github = github_downloads()
    thunderstore = thunderstore_downloads()
    gamebanana = gamebanana_downloads()
    nexus = nexus_downloads()

    total = github + thunderstore + gamebanana + nexus

    data = {
        "schemaVersion": 1,
        "label": "Total Downloads",
        "message": f"{total:,}",
        "color": "6c5ce7",

        "github": github,
        "thunderstore": thunderstore,
        "gamebanana": gamebanana,
        "nexus": nexus,
        "total": total,
    }

    with open(
        "downloads-badge.json",
        "w",
        encoding="utf-8"
    ) as file:
        json.dump(data, file, indent=2)

    print()
    print("========== DOWNLOADS ==========")
    print(f"GitHub:       {github:,}")
    print(f"Thunderstore: {thunderstore:,}")
    print(f"GameBanana:   {gamebanana:,}")
    print(f"Nexus Mods:   {nexus:,}")
    print("--------------------------------")
    print(f"TOTAL:        {total:,}")
    print("================================")


if __name__ == "__main__":
    main()
