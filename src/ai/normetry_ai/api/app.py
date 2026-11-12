from fastapi import FastAPI

from normetry_ai import telemetry
from normetry_ai.api import health
from normetry_ai.api.auth import InternalTokenMiddleware
from normetry_ai.settings import Settings


def create_app(settings: Settings | None = None) -> FastAPI:
    """Build the API. Raises pydantic's ValidationError when the internal token is missing or short.

    Run with: uv run uvicorn normetry_ai.api:create_app --factory
    """
    if settings is None:
        settings = Settings()

    # No OpenAPI or docs routes: an internal service has nothing to advertise.
    app = FastAPI(title="Normetry AI", docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(InternalTokenMiddleware, token=settings.internal_token)
    app.include_router(health.router)
    telemetry.instrument(app)
    return app
