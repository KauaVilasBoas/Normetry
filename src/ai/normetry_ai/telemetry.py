"""OpenTelemetry traces over OTLP, only when a collector is set up (Aspire sets the endpoint)."""

import os

from fastapi import FastAPI
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.sdk.resources import SERVICE_NAME, Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.util.http import (
    OTEL_INSTRUMENTATION_HTTP_CAPTURE_HEADERS_SANITIZE_FIELDS,
    get_custom_headers,
)

from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER

# Redacted even if header capture is switched on through OTEL_* variables.
_ALWAYS_SANITIZED = [INTERNAL_TOKEN_HEADER, "authorization", "cookie", "set-cookie"]


def instrument(app: FastAPI) -> None:
    if not os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"):
        return

    # Aspire sets OTEL_SERVICE_NAME to its resource name so the dashboard can match the traces.
    service_name = os.environ.get("OTEL_SERVICE_NAME", "normetry-ai")
    provider = TracerProvider(resource=Resource.create({SERVICE_NAME: service_name}))
    # The exporter reads endpoint, headers and certificate from the standard OTEL_* variables.
    provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
    trace.set_tracer_provider(provider)
    FastAPIInstrumentor.instrument_app(
        app,
        tracer_provider=provider,
        # Same as the .NET service defaults: health probes are not traced.
        excluded_urls="/health",
        # Passing this list replaces the env var, so the env var's fields are merged back in.
        http_capture_headers_sanitize_fields=_ALWAYS_SANITIZED
        + get_custom_headers(OTEL_INSTRUMENTATION_HTTP_CAPTURE_HEADERS_SANITIZE_FIELDS),
    )
