"""Provider adapters for browser-based authentication."""

from __future__ import annotations

import os
import re
import secrets
from dataclasses import dataclass
from typing import Any, Protocol
from urllib.parse import urlencode, urlsplit

import jwt
import requests
from jwt import PyJWKClient


_DEMO_IDENTITIES = {
    "student": ("demo-student", "student@example.test", "Demo Student"),
    "tutor": ("demo-tutor", "tutor@example.test", "Demo Tutor"),
    "tutee": ("demo-tutee", "tutee@example.test", "Demo Tutee"),
    "teacher": ("demo-teacher", "teacher@example.test", "Demo Teacher"),
    "admin": ("demo-admin", "admin@example.test", "Demo Admin"),
}
_PROVIDER_ID_PATTERN = re.compile(r"^[a-z][a-z0-9_-]{0,31}$")


class ProviderError(Exception):
    def __init__(self, reason_code: str):
        super().__init__(reason_code)
        self.reason_code = reason_code


@dataclass(frozen=True)
class ProviderIdentity:
    provider: str
    subject: str
    email: str
    display_name: str


class ProviderAdapter(Protocol):
    provider_id: str
    label: str

    def authorization_url(
        self,
        *,
        state: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        identity_hint: str | None = None,
    ) -> str: ...

    def authenticate(
        self,
        *,
        code: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        callback_url: str,
    ) -> ProviderIdentity: ...


class DevProviderAdapter:
    provider_id = "dev"
    label = "Development sandbox"

    def authorization_url(
        self,
        *,
        state: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        identity_hint: str | None = None,
    ) -> str:
        if identity_hint not in _DEMO_IDENTITIES:
            raise ProviderError("invalid_demo_identity")
        callback_path = urlsplit(redirect_uri).path
        return f"{callback_path}?{urlencode({'code': identity_hint, 'state': state})}"

    def authenticate(
        self,
        *,
        code: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        callback_url: str,
    ) -> ProviderIdentity:
        values = _DEMO_IDENTITIES.get(code)
        if values is None:
            raise ProviderError("invalid_demo_identity")
        subject, email, display_name = values
        return ProviderIdentity(self.provider_id, subject, email, display_name)


class FakeProviderAdapter:
    """Test adapter with fixed identities and no external network calls."""

    def __init__(
        self,
        provider_id: str = "fake",
        identities: dict[str, ProviderIdentity] | None = None,
        label: str = "Fake provider",
    ):
        self.provider_id = provider_id
        self.label = label
        self.identities = identities or {}

    def authorization_url(
        self,
        *,
        state: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        identity_hint: str | None = None,
    ) -> str:
        return f"{redirect_uri}?{urlencode({'state': state, 'code': identity_hint or 'default'})}"

    def authenticate(
        self,
        *,
        code: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        callback_url: str,
    ) -> ProviderIdentity:
        identity = self.identities.get(code)
        if identity is None:
            raise ProviderError("invalid_credentials")
        return identity


class OpenIDConnectAdapter:
    """OIDC authorization-code adapter with PKCE and strict ID-token checks."""

    def __init__(self, settings: dict[str, str], *, http: Any = requests):
        self.provider_id = settings["OIDC_PROVIDER_ID"]
        self.label = settings.get("OIDC_PROVIDER_LABEL", self.provider_id.title())
        self.issuer = settings["OIDC_ISSUER"].rstrip("/")
        self.client_id = settings["OIDC_CLIENT_ID"]
        self.client_secret = settings["OIDC_CLIENT_SECRET"]
        self.authorization_endpoint = settings["OIDC_AUTHORIZATION_ENDPOINT"]
        self.token_endpoint = settings["OIDC_TOKEN_ENDPOINT"]
        self.jwks_uri = settings["OIDC_JWKS_URI"]
        self.redirect_uri = settings["OIDC_REDIRECT_URI"]
        self.redirect_allowlist = frozenset(
            item.strip()
            for item in settings["OIDC_REDIRECT_URI_ALLOWLIST"].split(",")
            if item.strip()
        )
        self.http = http

    def validate_redirect_uri(self, redirect_uri: str, callback_url: str) -> None:
        if (
            redirect_uri != self.redirect_uri
            or redirect_uri not in self.redirect_allowlist
            or callback_url != redirect_uri
        ):
            raise ProviderError("redirect_uri_not_allowed")

    def authorization_url(
        self,
        *,
        state: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        identity_hint: str | None = None,
    ) -> str:
        self.validate_redirect_uri(redirect_uri, redirect_uri)
        import hashlib
        import base64

        challenge = base64.urlsafe_b64encode(
            hashlib.sha256(verifier.encode("ascii")).digest()
        ).rstrip(b"=").decode("ascii")
        query = urlencode({
            "client_id": self.client_id,
            "redirect_uri": redirect_uri,
            "response_type": "code",
            "scope": "openid email profile",
            "state": state,
            "nonce": nonce,
            "code_challenge": challenge,
            "code_challenge_method": "S256",
        })
        separator = "&" if "?" in self.authorization_endpoint else "?"
        return f"{self.authorization_endpoint}{separator}{query}"

    def authenticate(
        self,
        *,
        code: str,
        nonce: str,
        verifier: str,
        redirect_uri: str,
        callback_url: str,
    ) -> ProviderIdentity:
        self.validate_redirect_uri(redirect_uri, callback_url)
        try:
            response = self.http.post(
                self.token_endpoint,
                data={
                    "grant_type": "authorization_code",
                    "code": code,
                    "redirect_uri": redirect_uri,
                    "client_id": self.client_id,
                    "client_secret": self.client_secret,
                    "code_verifier": verifier,
                },
                timeout=10,
            )
            response.raise_for_status()
            token_response = response.json()
            id_token = token_response["id_token"]
            signing_key = PyJWKClient(
                self.jwks_uri,
                timeout=10,
            ).get_signing_key_from_jwt(id_token).key
            claims = jwt.decode(
                id_token,
                signing_key,
                algorithms=["RS256"],
                issuer=self.issuer,
                audience=self.client_id,
                options={"require": ["exp", "iss", "aud", "sub", "nonce"]},
            )
        except (requests.RequestException, jwt.PyJWTError, KeyError, TypeError, ValueError) as error:
            raise ProviderError("provider_response_invalid") from error

        token_nonce = claims.get("nonce")
        subject = claims.get("sub")
        email = claims.get("email")
        display_name = claims.get("name")
        if (
            not isinstance(token_nonce, str)
            or not secrets.compare_digest(token_nonce, nonce)
            or not isinstance(subject, str)
            or not subject
            or len(subject) > 191
            or not isinstance(email, str)
            or not email
            or len(email) > 254
            or claims.get("email_verified") is not True
            or not isinstance(display_name, str)
            or not display_name.strip()
            or len(display_name) > 120
        ):
            raise ProviderError("provider_identity_invalid")
        return ProviderIdentity(self.provider_id, subject, email, display_name.strip())


def truthy(value: str | None) -> bool:
    return value is not None and value.strip().lower() in {"1", "true", "yes", "on"}


def build_provider_registry(
    environ: dict[str, str] | None = None,
    *,
    oidc_http: Any = requests,
) -> dict[str, ProviderAdapter]:
    env = os.environ if environ is None else environ
    dev_enabled = truthy(env.get("DEV_LOGIN_ENABLED"))
    production = (
        env.get("APP_ENV", "").strip().lower() == "production"
        or env.get("NODE_ENV", "").strip().lower() == "production"
    )
    if dev_enabled and production:
        raise RuntimeError("DEV_LOGIN_ENABLED cannot be enabled in production.")

    providers: dict[str, ProviderAdapter] = {}
    if dev_enabled:
        providers["dev"] = DevProviderAdapter()

    oidc_keys = (
        "OIDC_PROVIDER_ID",
        "OIDC_ISSUER",
        "OIDC_CLIENT_ID",
        "OIDC_CLIENT_SECRET",
        "OIDC_AUTHORIZATION_ENDPOINT",
        "OIDC_TOKEN_ENDPOINT",
        "OIDC_JWKS_URI",
        "OIDC_REDIRECT_URI",
        "OIDC_REDIRECT_URI_ALLOWLIST",
    )
    if all(env.get(key, "").strip() for key in oidc_keys):
        provider_id = env["OIDC_PROVIDER_ID"]
        issuer_host = urlsplit(env["OIDC_ISSUER"]).hostname
        if (
            not _PROVIDER_ID_PATTERN.fullmatch(provider_id)
            or provider_id in {"dev", "google"}
            or issuer_host == "accounts.google.com"
        ):
            raise RuntimeError(
                "Google is reserved for DBSERV; HOST OIDC must use another provider."
            )
        provider = OpenIDConnectAdapter(env, http=oidc_http)
        if provider.redirect_uri not in provider.redirect_allowlist:
            raise RuntimeError("OIDC_REDIRECT_URI must exactly match an allowed redirect URI.")
        providers[provider_id] = provider
    return providers
