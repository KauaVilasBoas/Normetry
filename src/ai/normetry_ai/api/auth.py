"""Internal-token check in front of everything: only YARP knows the token."""

import hmac

from pydantic import SecretStr
from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Receive, Scope, Send
from starlette.websockets import WebSocketClose

# Contract with the core's YARP gateway: src/shared/contracts/internal-http.md.
INTERNAL_TOKEN_HEADER = "X-Normetry-Internal-Token"  # noqa: S105 - header name, not a secret
_HEADER_KEY = INTERNAL_TOKEN_HEADER.lower().encode()  # ASGI servers lowercase header names
_DETAIL = "Missing or invalid internal token."


class InternalTokenMiddleware:
    """Pure ASGI middleware: rejects http and websocket scopes before routing and body parsing.

    Exactly one token header is accepted; lifespan and other scopes pass through.
    """

    def __init__(self, app: ASGIApp, token: SecretStr) -> None:
        self.app = app
        self._token = token.get_secret_value().encode()

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] in ("http", "websocket") and not self._authorized(scope):
            if scope["type"] == "http":
                response = JSONResponse({"detail": _DETAIL}, status_code=401)
            else:
                response = WebSocketClose(code=1008)  # policy violation; servers answer 403
            await response(scope, receive, send)
            return
        await self.app(scope, receive, send)

    def _authorized(self, scope: Scope) -> bool:
        values = [value for key, value in scope["headers"] if key == _HEADER_KEY]
        return len(values) == 1 and hmac.compare_digest(values[0], self._token)
