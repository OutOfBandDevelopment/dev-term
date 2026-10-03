FROM alpine:3.20
RUN apk add --no-cache socat
COPY device.sh /device.sh
RUN chmod +x /device.sh
EXPOSE 7777
ENTRYPOINT ["socat", "TCP-LISTEN:7777,reuseaddr,fork", "EXEC:/device.sh"]
