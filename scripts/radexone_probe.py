"""Sends the Radex One's three read requests over a serial port and prints the raw bytes sent and received.
Usage: python scripts/radexone_probe.py COM8. Used by docs/test reports to capture real replies."""
import struct
import sys
import time

import serial


def checksum(data: bytes) -> int:
    total = sum(struct.unpack("<%dH" % (len(data) // 2), data))
    return 0xFFFF - (total % 0xFFFF)


def request(packet_number: int, code: int, word: int = 0x000C) -> bytes:
    ext = struct.pack("<HH", code, word)
    ext += struct.pack("<H", checksum(ext))
    head = struct.pack("<BBHHHH", 0x7B, 0xFF, 0x0020, len(ext), packet_number, 0)
    return head + struct.pack("<H", checksum(head)) + ext


def main(port: str) -> None:
    with serial.Serial(port, 9600, timeout=1.5, dsrdtr=False) as s:
        s.dtr = s.rts = True
        for name, code in (("ReadData", 0x0800), ("ReadSerialVersion", 0x0001), ("ReadSettings", 0x0801)):
            req = request(1, code)
            s.reset_input_buffer()
            s.write(req)
            time.sleep(0.7)
            reply = s.read(s.in_waiting)
            print(f"{name}\n  sent: {req.hex(' ').upper()}\n  recv({len(reply)}): {reply.hex(' ').upper()}")


main(sys.argv[1])
