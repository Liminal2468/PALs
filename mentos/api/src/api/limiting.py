"""File: limiting.py
Description: Configure per-client rate limits for the HOST API.
Version: 1.0.0
"""

from flask_limiter import Limiter
from flask_limiter.util import get_remote_address


limiter = Limiter(
    key_func=get_remote_address,
    default_limits=["120 per 15 minutes"],
    storage_uri="memory://",
)
