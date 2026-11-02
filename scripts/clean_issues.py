import os, re
from difflib import SequenceMatcher

import requests

TOKEN = os.environ["GITHUB_TOKEN"]
REPO = os.environ["REPO"]
API = f"https://api.github.com/repos/{REPO}"
HEADERS = {"Authorization": f"Bearer {TOKEN}", "Accept": "application/vnd.github+json"}

COUNT_SUFFIX = re.compile(r"\s*x(\d+)$")
SIMILARITY_THRESHOLD = 0.85


def base_title(title):
    return COUNT_SUFFIX.sub("", title).strip()


def count_of(title):
    m = COUNT_SUFFIX.search(title)
    return int(m.group(1)) if m else 1


def normalize(text):
    return re.sub(r"\s+", " ", (text or "").lower()).strip()


def is_same_issue(a, b):
    if normalize(base_title(a["title"])) != normalize(base_title(b["title"])):
        return False
    return SequenceMatcher(None, normalize(a["body"]), normalize(b["body"])).ratio() >= SIMILARITY_THRESHOLD


def list_open_issues():
    issues, page = [], 1
    while True:
        r = requests.get(f"{API}/issues", headers=HEADERS, params={"state": "open", "per_page": 100, "page": page})
        r.raise_for_status()
        data = r.json()
        if not data:
            return issues
        issues += [i for i in data if "pull_request" not in i]
        page += 1


def group_duplicates(issues):
    groups = []
    for issue in issues:
        for group in groups:
            if is_same_issue(group[0], issue):
                group.append(issue)
                break
        else:
            groups.append([issue])
    return groups


def main():
    for items in group_duplicates(list_open_issues()):
        if len(items) < 2:
            continue

        items.sort(key=lambda i: i["created_at"])
        keeper, duplicates = items[0], items[1:]
        new_title = f"{base_title(keeper['title'])} x{sum(count_of(i['title']) for i in items)}"

        requests.patch(f"{API}/issues/{keeper['number']}", headers=HEADERS, json={"title": new_title}).raise_for_status()

        for dup in duplicates:
            requests.post(f"{API}/issues/{dup['number']}/comments", headers=HEADERS, json={"body": f"Regroupé dans #{keeper['number']}."}).raise_for_status()
            requests.patch(f"{API}/issues/{dup['number']}", headers=HEADERS, json={"state": "closed", "state_reason": "not_planned"}).raise_for_status()

        print(f"#{keeper['number']} -> {new_title} ({len(duplicates)} fermées)")


if __name__ == "__main__":
    main()
