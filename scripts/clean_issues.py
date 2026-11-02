import os
import re
from collections import defaultdict

import requests

TOKEN = os.environ["GITHUB_TOKEN"]
REPO = os.environ["REPO"]
API = f"https://api.github.com/repos/{REPO}"
HEADERS = {
    "Authorization": f"Bearer {TOKEN}",
    "Accept": "application/vnd.github+json",
}

COUNT_SUFFIX = re.compile(r"\s*x(\d+)$")


def base_title(title):
    return COUNT_SUFFIX.sub("", title).strip()


def count_of(title):
    m = COUNT_SUFFIX.search(title)
    return int(m.group(1)) if m else 1


def list_open_issues():
    issues, page = [], 1
    while True:
        r = requests.get(
            f"{API}/issues",
            headers=HEADERS,
            params={"state": "open", "per_page": 100, "page": page},
        )
        r.raise_for_status()
        data = r.json()
        if not data:
            break
        issues += [i for i in data if "pull_request" not in i]
        page += 1
    return issues


def main():
    groups = defaultdict(list)
    for issue in list_open_issues():
        key = base_title(issue["title"]).lower()
        groups[key].append(issue)

    for items in groups.values():
        if len(items) < 2:
            continue

        items.sort(key=lambda i: i["created_at"])
        keeper, duplicates = items[0], items[1:]
        total = sum(count_of(i["title"]) for i in items)
        new_title = f"{base_title(keeper['title'])} x{total}"

        r = requests.patch(
            f"{API}/issues/{keeper['number']}",
            headers=HEADERS,
            json={"title": new_title},
        )
        r.raise_for_status()

        for dup in duplicates:
            requests.post(
                f"{API}/issues/{dup['number']}/comments",
                headers=HEADERS,
                json={"body": f"Regroupé dans #{keeper['number']}."},
            ).raise_for_status()
            requests.patch(
                f"{API}/issues/{dup['number']}",
                headers=HEADERS,
                json={"state": "closed", "state_reason": "not_planned"},
            ).raise_for_status()

        print(f"#{keeper['number']} -> {new_title} ({len(duplicates)} fermées)")


if __name__ == "__main__":
    main()
