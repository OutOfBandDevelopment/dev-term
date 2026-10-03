#!/bin/sh
# A scripted line-based TCP device: the same commands as dev-term's built-in loopback transport, so the two can be compared.
# One process per connection; its counters start at 0 for each client.
status=0
while IFS= read -r line; do
  line=$(printf '%s' "$line" | tr -d '\r')
  case "$line" in
    hello|HELLO) echo "From Loopback test" ;;
    "STATUS?")
      awk -v n="$status" 'BEGIN { printf "temp=%.2f C volts=%.2f V state=%s\n", 21.5 + 0.25 * (n % 8), 3.3 - 0.01 * (n % 5), (n % 5 == 4 ? "WARN" : "OK") }'
      status=$((status + 1)) ;;
    "help"|"?")
      echo "Commands: hello, STATUS?, help" ;;
    *) echo "ERR unknown command: $line" ;;
  esac
done
