import os, re
from collections import defaultdict
from difflib import SequenceMatcher

import requests

API = f"https://api.github.com/repos/{os.environ['REPO']}"
BOT = os.environ["REPORTER_LOGIN"].strip().casefold()
APPLY = os.getenv("APPLY", "").strip() == "1"
SIMILARITY = 0.90

REPORT_TYPE = re.compile(r"^Type:[ \t]*exception[ \t]*\r?$", re.I | re.M)
ERROR_HEADER = re.compile(r"^Error:[ \t]*\r?$", re.I | re.M)
EXCEPTION = re.compile(r"\b[\w.]+(?:Exception|Error):")
FRAME = re.compile(r"^(?:at\s+|UnityEngine\.)", re.I)
LOCATION = re.compile(r"\s+\[0x[0-9a-f]+\]|\s+\(at [^)]*\)|\s+in\s+<[^>]+>:\d+", re.I)


def signature(issue):
    body = issue.get("body") or ""
    header = ERROR_HEADER.search(body)
    if not header or not REPORT_TYPE.search(body, 0, header.start()):
        return None

    lines = [line.strip(" `\\") for line in body[header.end():].splitlines()]
    error = next((line for line in lines if EXCEPTION.search(line)), "")
    frames = tuple(" ".join(LOCATION.sub("", line).split()) for line in lines if FRAME.match(line))
    return (" ".join(error.split()), frames) if error and frames else None


def github(session, method, url, **kwargs):
    response = session.request(method, url, timeout=30, **kwargs)
    response.raise_for_status()
    return response


def pages(session, url, **params):
    while url:
        response = github(session, "GET", url, params=params)
        yield from response.json()
        url, params = response.links.get("next", {}).get("url"), {}


def is_bot_issue(issue):
    return "pull_request" not in issue and (issue.get("user") or {}).get("login", "").casefold() == BOT


def close_duplicate(session, original, duplicate, expected):
    url = f"{API}/issues/{duplicate}"
    current = github(session, "GET", url).json()
    if not is_bot_issue(current) or current["state"] != "open" or signature(current) != expected:
        print(f"Skipped #{duplicate}: issue changed")
        return

    comments_url, marker = f"{url}/comments", f"<!-- ultimatemenu-duplicate:{original} -->"
    linked = any(
        (comment.get("body") or "").rstrip().endswith(marker)
        for comment in pages(session, comments_url, per_page=100)
    )
    if not linked:
        body = f"Duplicate crash report. Tracking this in #{original}.\n\n{marker}"
        github(session, "POST", comments_url, json={"body": body})
    github(session, "PATCH", url, json={"state": "closed", "state_reason": "not_planned"})


def main():
    if not BOT.endswith("[bot]"):
        raise SystemExit("Set REPORTER_LOGIN to the exact GitHub App bot login.")

    groups, candidates = defaultdict(list), defaultdict(list)
    with requests.Session() as session:
        session.headers.update({
            "Authorization": f"Bearer {os.environ['GITHUB_TOKEN']}",
            "Accept": "application/vnd.github+json",
        })

        for issue in pages(session, f"{API}/issues", state="open", per_page=100):
            if not is_bot_issue(issue):
                continue
            key = signature(issue)
            if key:
                groups[key].append(issue["number"])
            else:
                print(f"Review #{issue['number']}: no usable crash signature")

        print("Mode:", "apply" if APPLY else "preview")
        for key, numbers in groups.items():
            original, *duplicates = sorted(numbers)
            error, frames = key
            candidates[error, frames[0]].append((original, frames))
            for duplicate in duplicates:
                print(f"Duplicate #{duplicate} → #{original}")
                if APPLY:
                    close_duplicate(session, original, duplicate, key)

        for bucket in candidates.values():
            for index, (number, frames) in enumerate(bucket):
                for other, other_frames in bucket[index + 1:]:
                    if 2 * min(len(frames), len(other_frames)) < SIMILARITY * (len(frames) + len(other_frames)):
                        continue
                    score = SequenceMatcher(None, frames, other_frames, autojunk=False).ratio()
                    if score >= SIMILARITY:
                        print(f"Review #{number} and #{other}: {score:.0%} stack similarity")


if __name__ == "__main__":
    main()
