# Test containers

Disposable services for integration tests that need a real peer. Nothing here is for production.

```bash
docker compose -f containers/docker-compose.yml up -d     # start
docker compose -f containers/docker-compose.yml down      # stop and discard
```

| Service | Endpoint (localhost) | Used for |
|---|---|---|
| mosquitto | `1883` MQTT, anonymous | MQTT transport |
| rabbitmq | `5672` AMQP 0-9-1, `21613` STOMP (61613 is in a Windows reserved range), `1884` MQTT, `15672` UI; user/password `devterm` | AMQP / STOMP transports |
| ser2net | `2217` RFC 2217 | RFC 2217 client; the port behind it echoes every byte |

| aspire-dashboard | `18888` UI, `4317` OTLP/gRPC, no auth | Viewing `dev-term --otlp true` traces and metrics; not used by tests |

Integration tests that use these skip as Inconclusive when the port is unreachable, like the real-hardware tests.
