"""Authorize MangaHub locally. Requires PyJWT with its crypto extra."""
import argparse
import base64
import hashlib
import hmac
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
import secrets
import urllib.parse
import urllib.request
import webbrowser
from datetime import datetime, timedelta, timezone

import jwt

ISSUER = "https://auth.openai.com"
RESOURCE = "https://api.openai.com/v1"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", help="New protected credential file; existing files are never overwritten")
    parser.add_argument("--host-id", required=True, help="Stable urn:uuid identifier for this local installation")
    args = parser.parse_args()
    if os.path.lexists(args.output):
        parser.error("Output already exists.")
    if os.name == "nt":
        print("Use a private directory accessible only to your Windows account.")
    state, nonce, verifier = (secrets.token_urlsafe(48) for _ in range(3))
    received = {}

    class Callback(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            sent = query.get("state", [""])[0]
            if parsed.path != "/auth/callback" or not hmac.compare_digest(sent, state):
                self.send_error(400, "Invalid callback")
                return
            received.update(query)
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Referrer-Policy", "no-referrer")
            self.end_headers()
            self.wfile.write(b"Return to the MangaHub setup terminal.")

    with HTTPServer(("127.0.0.1", 0), Callback) as server:
        server.timeout = 1
        redirect = f"http://127.0.0.1:{server.server_port}/auth/callback"
        challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("=")
        query = urllib.parse.urlencode(dict(client_id="dynamic_agent_client", agent_name_hint="MangaHub",
            ext_agent_host_id=args.host_id, response_type="code", redirect_uri=redirect,
            scope="openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            resource=RESOURCE, state=state, nonce=nonce, code_challenge_method="S256", code_challenge=challenge))
        if not webbrowser.open(ISSUER + "/api/accounts/authorize?" + query):
            raise RuntimeError("Could not open the system browser.")
        deadline = datetime.now(timezone.utc) + timedelta(minutes=5)
        while not received and datetime.now(timezone.utc) < deadline:
            server.handle_request()

    if "error" in received or "code" not in received or "client_id" not in received:
        raise RuntimeError("Authorization was declined, incomplete, or timed out.")
    client_id = received["client_id"][0]
    if not client_id.startswith("oaiapp_"):
        raise RuntimeError("OpenAI did not return an issued client ID.")
    body = urllib.parse.urlencode(dict(grant_type="authorization_code", client_id=client_id,
        code=received["code"][0], code_verifier=verifier, redirect_uri=redirect, resource=RESOURCE)).encode()
    with urllib.request.urlopen(ISSUER + "/api/accounts/oauth/token", body, timeout=20) as response:
        tokens = json.load(response)
    with urllib.request.urlopen(ISSUER + "/.well-known/openid-configuration", timeout=20) as response:
        discovery = json.load(response)
    if discovery["issuer"] != ISSUER or urllib.parse.urlsplit(discovery["jwks_uri"]).netloc != "auth.openai.com":
        raise RuntimeError("Unexpected OpenAI identity metadata.")
    id_token = tokens["id_token"]
    key = jwt.PyJWKClient(discovery["jwks_uri"], timeout=20).get_signing_key_from_jwt(id_token)
    identity = jwt.decode(id_token, key.key, algorithms=["RS256"], issuer=ISSUER, audience=client_id,
        options={"require": ["exp", "sub", "nonce"]})
    scopes = tokens.get("scope", "").split()
    if not hmac.compare_digest(identity["nonce"], nonce) or "chatgpt.tokens.use.direct" not in scopes:
        raise RuntimeError("Identity or ChatGPT plan permission could not be verified.")
    saved = dict(client_id=client_id, access_token=tokens["access_token"], refresh_token=tokens["refresh_token"],
        expires_at=(datetime.now(timezone.utc) + timedelta(seconds=tokens["expires_in"])).isoformat())
    with os.fdopen(os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as output:
        json.dump(saved, output)
    print("MangaHub authorization saved. Transfer the protected file to the NAS over SSH.")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        raise SystemExit(f"MangaHub authorization failed ({type(exc).__name__}).") from None
