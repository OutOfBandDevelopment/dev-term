FROM alpine:3.20
RUN apk add --no-cache ser2net socat
COPY ser2net.conf /etc/ser2net.conf
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
EXPOSE 2217
ENTRYPOINT ["/entrypoint.sh"]
