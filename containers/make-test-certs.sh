#!/bin/sh
# Generates a throwaway CA and a localhost server certificate for the TLS listeners (certs/ is git-ignored).
set -e
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")"
mkdir -p certs
cd certs
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=DevTerm Test CA" -keyout ca.key -out ca.pem
openssl req -newkey rsa:2048 -nodes -subj "/CN=localhost" -keyout server.key -out server.csr
printf 'subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > san.ext
openssl x509 -req -in server.csr -CA ca.pem -CAkey ca.key -CAcreateserial -days 30 -extfile san.ext -out server.pem
chmod 644 *.pem *.key
rm -f server.csr san.ext ca.srl
