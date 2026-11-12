"""HTTP API, reachable only through the core's YARP gateway (`/ai/*`)."""

from normetry_ai.api.app import create_app

__all__ = ["create_app"]
