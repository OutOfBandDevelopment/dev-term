#!/bin/sh
# Virtual serial pair: ser2net owns /tmp/ttyA, an echo loop owns /tmp/ttyB.
socat -d pty,raw,echo=0,link=/tmp/ttyA pty,raw,echo=0,link=/tmp/ttyB &
while [ ! -e /tmp/ttyB ]; do sleep 0.2; done
cat /tmp/ttyB > /tmp/ttyB &
exec ser2net -n -c /etc/ser2net.conf
