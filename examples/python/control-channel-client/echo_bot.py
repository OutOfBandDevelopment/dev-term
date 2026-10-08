"""Attach to a running dev-term session over --controlhttp and answer what the device sends.

Run dev-term with:  --transport loopback --cli true --controlhttp 8765 --controltoken secret
Then:               python echo_bot.py 8765 secret

Every received line that contains "Loopback" is answered with "world" via POST /command.
Standard library only.
"""
import sys
import urllib.request

port, token = sys.argv[1], sys.argv[2]
base = f"http://127.0.0.1:{port}"
auth = {"Authorization": f"Bearer {token}"}


def command(text):
    request = urllib.request.Request(base + "/command", data=text.encode(), headers=auth, method="POST")
    with urllib.request.urlopen(request, timeout=5) as response:
        return response.read().decode()


print("ping:", command("ping"), flush=True)
with urllib.request.urlopen(urllib.request.Request(base + "/events", headers=auth)) as events:
    for raw in events:
        line = raw.decode().strip()
        if not line.startswith("data: "):
            continue
        event = line[6:]
        print(event, flush=True)
        if event.startswith("rx "):
            text = bytes.fromhex(event[3:]).decode(errors="replace")
            if "Loopback" in text:
                print("reply:", command("send world"), flush=True)
        elif event.startswith("closed"):
            break
