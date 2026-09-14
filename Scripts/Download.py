import json
import os
import requests

GITHUB_REPO = "MATTRAX64/UltimateMenu"

THUNDERSTORE_URL = (
    "https://thunderstore.io/c/gorilla-tag/api/v1/package/"
    "MATTRAX/UltimateMenu/"
)

GAMEBANANA_ID = "716098"
NEXUS_MOD_ID = "1330"


def github_downloads():
    url = f"https://api.github.com/repos/{GITHUB_REPO}/releases"
    response = requests.get(url, timeout=30)
    response.raise_for_status()

    total = 0

    for release in response.json():
        for asset in release.get("assets", []):
            total += asset.get("download_count", 0)

    return total


def thunderstore_downloads():
    response = requests.get(THUNDERSTORE_URL, timeout=30)
    response.raise_for_status()

    data = response.json()

    # Thunderstore package metadata contains version metrics.
    return sum(
        version.get("downloads", 0)
        for version in data.get("versions", [])
    )


def gamebanana_downloads():
    # GameBanana API endpoint for a mod.
    url = f"https://gamebanana.com/apiv11/Mod/{GAMEBANANA_ID}"
    response = requests.get(url, timeout=30)
    response.raise_for_status()

    data = response.json()

    return int(data.get("_sProfileUrl", 0) or 0)


def nexus_downloads():
    api_key = os.environ.get("NEXUS_API_KEY")

    if not api_key:
        print("NEXUS_API_KEY missing")
        return 0

    url = f"https://api.nexusmods.com/v1/games/gorillatag/mods/{NEXUS_MOD_ID}.json"

    headers = {
        "apikey": api_key,
        "accept": "application/json",
    }

    response = requests.get(url, headers=headers, timeout=30)
    response.raise_for_status()

    data = response.json()

    return int(data.get("mod_downloads", 0))


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

with open("downloads-badge.json", "w", encoding="utf-8") as file:
    json.dump(data, file, indent=2)

print(json.dumps(data, indent=2))