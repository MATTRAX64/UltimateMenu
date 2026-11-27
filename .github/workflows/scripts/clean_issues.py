import json, os, re
from collections import defaultdict
from difflib import SequenceMatcher
from itertools import combinations
from urllib.parse import urlencode
from urllib.request import Request, urlopen

NEXT_LINK = re.compile(r'<([^>]+)>\s*;\s*rel="next"')

API = f"https://api.github.com/repos/{os.environ['REPO']}"
BOT, APPLY = os.environ["REPORTER_LOGIN"].strip().casefold(), os.getenv("APPLY", "").strip() == "1"
SIMILARITY = 0.90
REPORT_TYPE = re.compile(r"^Type:[ \t]*exception[ \t]*\r?$", re.I | re.M)
ERROR_HEADER = re.compile(r"^Error:[ \t]*\r?$", re.I | re.M)
EXCEPTION = re.compile(r"\b[\w.]+(?:Exception|Error):")
FRAME = re.compile(r"^(?:at\s+|[\w.+`<>]+[.:][\w`<>]+\s*\()", re.I)
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

def github(headers, method, url, *, params=None, payload=None):
    if params:
        url += "?" + urlencode(params)
    data = None
    if payload is not None:
        headers = {**headers, "Content-Type": "application/json"}
        data = json.dumps(payload).encode("utf-8")
    request = Request(url, data=data, headers=headers, method=method)
    with urlopen(request, timeout=30) as response:
        match = NEXT_LINK.search(response.headers.get("Link", ""))
        return json.load(response), match.group(1) if match else None

def pages(headers, url, **params):
    while url:
        items, url = github(headers, "GET", url, params=params)
        yield from items
        params = {}

def is_bot_issue(issue):
    return "pull_request" not in issue and (issue.get("user") or {}).get("login", "").casefold() == BOT

def close_duplicate(headers, original, duplicate, expected):
    url = f"{API}/issues/{duplicate}"
    current = github(headers, "GET", url)[0]
    if not is_bot_issue(current) or current["state"] != "open" or signature(current) != expected:
        print(f"Skipped #{duplicate}: issue changed")
        return
    comments_url, marker = f"{url}/comments", f"<!-- ultimatemenu-duplicate:{original} -->"
    linked = any((c.get("body") or "").rstrip().endswith(marker) for c in pages(headers, comments_url, per_page=100))
    if not linked:
        body = f"Duplicate crash report. Tracking this in #{original}.\n\n{marker}"
        github(headers, "POST", comments_url, payload={"body": body})
    github(headers, "PATCH", url, payload={"state": "closed", "state_reason": "not_planned"})

def main():
    if not BOT.endswith("[bot]"):
        raise SystemExit("Set REPORTER_LOGIN to the exact GitHub App bot login.")
    groups, candidates = defaultdict(list), defaultdict(list)
    headers = {"Authorization": f"Bearer {os.environ['GITHUB_TOKEN']}", "Accept": "application/vnd.github+json"}
    for issue in pages(headers, f"{API}/issues", state="open", per_page=100):
        if not is_bot_issue(issue):
            continue
        key = signature(issue)
        if key:
            groups[key].append(issue["number"])
        else:
            print(f"Review #{issue['number']}: no usable crash signature")

    print("Mode:", "apply" if APPLY else "preview")
    for (error, frames), numbers in groups.items():
        original, *duplicates = sorted(numbers)
        candidates[error, frames[0]].append((original, frames))
        for duplicate in duplicates:
            print(f"Duplicate #{duplicate} → #{original}")
            if APPLY:
                close_duplicate(headers, original, duplicate, (error, frames))

    for bucket in candidates.values():
        for (number, frames), (other, other_frames) in combinations(bucket, 2):
            sizes = len(frames), len(other_frames)
            if 2 * min(sizes) < SIMILARITY * sum(sizes):
                continue
            score = SequenceMatcher(None, frames, other_frames, autojunk=False).ratio()
            if score >= SIMILARITY:
                print(f"Review #{number} and #{other}: {score:.0%} stack similarity")

if __name__ == "__main__":
    main()
