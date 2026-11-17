"""Memory-hard password hashing for local administrator credentials."""

from __future__ import annotations

import base64
import hashlib
import hmac
import secrets


_SCRYPT_N = 2**14
_SCRYPT_R = 8
_SCRYPT_P = 1
_SCRYPT_KEY_LENGTH = 64
_SCRYPT_MAX_MEMORY = 64 * 1024 * 1024


def _encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _decode(value: str) -> bytes:
    padded = value + "=" * (-len(value) % 4)
    return base64.b64decode(padded, altchars=b"-_", validate=True)


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    derived = hashlib.scrypt(
        password.encode("ascii"),
        salt=salt,
        n=_SCRYPT_N,
        r=_SCRYPT_R,
        p=_SCRYPT_P,
        dklen=_SCRYPT_KEY_LENGTH,
        maxmem=_SCRYPT_MAX_MEMORY,
    )
    return f"scrypt${_SCRYPT_N}${_SCRYPT_R}${_SCRYPT_P}${_encode(salt)}${_encode(derived)}"


def verify_password(password: str, encoded_hash: str, fallback_hash: str) -> bool:
    try:
        algorithm, n_text, r_text, p_text, salt_text, expected_text = encoded_hash.split("$")
        if (
            algorithm != "scrypt"
            or int(n_text) != _SCRYPT_N
            or int(r_text) != _SCRYPT_R
            or int(p_text) != _SCRYPT_P
        ):
            raise ValueError("Unsupported password hash.")
        salt = _decode(salt_text)
        expected = _decode(expected_text)
        if len(salt) != 16 or len(expected) != _SCRYPT_KEY_LENGTH:
            raise ValueError("Malformed password hash.")
    except (AttributeError, ValueError):
        if encoded_hash != fallback_hash:
            return verify_password(password, fallback_hash, fallback_hash)
        return False

    derived = hashlib.scrypt(
        password.encode("ascii"),
        salt=salt,
        n=_SCRYPT_N,
        r=_SCRYPT_R,
        p=_SCRYPT_P,
        dklen=_SCRYPT_KEY_LENGTH,
        maxmem=_SCRYPT_MAX_MEMORY,
    )
    return hmac.compare_digest(derived, expected)

