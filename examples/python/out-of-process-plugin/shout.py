"""Example out-of-process dev-term presenter: upper-cases whatever text the device sent.
Protocol (JSON lines on stdin/stdout): see docs/design/proposals/out-of-process-plugins.md."""
import json
import sys

for raw in sys.stdin:
    message = json.loads(raw)
    if message["type"] == "hello":
        reply = {"type": "hello", "name": "py-shout", "protocol": 1}
    elif message["type"] == "render":
        text = bytes.fromhex(message["hex"]).decode("ascii", errors="replace")
        reply = {"type": "output", "lines": [text.upper()]}
    else:
        reply = {"type": "output", "lines": []}
    print(json.dumps(reply), flush=True)
