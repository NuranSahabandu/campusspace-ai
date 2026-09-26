"""X-Service-Key check for routes that only the .NET API may call (plan §7.1 rule 2)."""

import hmac

from fastapi import Header, HTTPException, Request, status


def require_service_key(
    request: Request,
    x_service_key: str | None = Header(default=None, alias="X-Service-Key"),
) -> None:
    expected = request.app.state.settings.service_key.get_secret_value()
    # Constant-time comparison; the key is never logged.
    if x_service_key is None or not hmac.compare_digest(x_service_key.encode(), expected.encode()):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid service key")
