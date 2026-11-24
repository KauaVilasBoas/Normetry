"""Who is calling, read from the internal headers the core's gateway sets.

Contract: src/shared/contracts/internal-http.md. InternalTokenMiddleware runs before routing, so a
route that depends on request_context only ever sees headers that came with a valid token.
"""

from dataclasses import dataclass, field
from uuid import UUID

from fastapi import HTTPException
from starlette.requests import HTTPConnection

from normetry_ai.retrieval.policy import JURISDICTIONS

TENANT_HEADER = "X-Normetry-Tenant-Id"
USER_HEADER = "X-Normetry-User-Id"
ESTABLISHMENT_HEADER = "X-Normetry-Establishment-Id"
JURISDICTION_HEADER = "X-Normetry-Jurisdiction"
# Set by the gateway to `true` only when the caller holds documents.read.
DOCUMENTS_READ_HEADER = "X-Normetry-Documents-Read"
_DETAIL = "Missing or invalid internal context headers."


@dataclass(frozen=True)
class RequestContext:
    tenant_id: UUID
    user_id: str
    # Whether the caller may read the tenant's documents (documents.read): only then does the
    # chat search them or show their citations. False unless the gateway says so.
    documents_read: bool = field(default=False, kw_only=True)


@dataclass(frozen=True)
class EstablishmentContext(RequestContext):
    """The caller plus the establishment the core loaded under RLS and its jurisdiction."""

    establishment_id: UUID
    jurisdiction: str


def _one(request: HTTPConnection, header: str) -> str:
    values = request.headers.getlist(header)
    if len(values) != 1 or not values[0]:
        raise HTTPException(status_code=400, detail=_DETAIL)
    return values[0]


def _uuid(value: str) -> UUID:
    """Only the non-nil, lower-case 8-4-4-4-12 form the gateway writes; UUID() also takes braces,
    urn:, upper case and no dashes."""
    try:
        parsed = UUID(value)
    except ValueError:
        raise HTTPException(status_code=400, detail=_DETAIL) from None
    if str(parsed) != value or parsed.int == 0:
        raise HTTPException(status_code=400, detail=_DETAIL)
    return parsed


def _documents_read(request: HTTPConnection) -> bool:
    """True only for exactly one documents-read header holding `true`: absent, repeated or any
    other value is false, so a gateway that does not send it shows no document."""
    return request.headers.getlist(DOCUMENTS_READ_HEADER) == ["true"]


def request_context(request: HTTPConnection) -> RequestContext:
    """FastAPI dependency: exactly one tenant header (a non-nil UUID) and one non-empty user header,
    and whether the caller may read documents.

    Anything else is a gateway bug, never something to guess around, so it answers 400.
    HTTPConnection rather than Request, so websocket routes can depend on it too.
    """
    tenant = _one(request, TENANT_HEADER)
    user = _one(request, USER_HEADER)
    return RequestContext(
        tenant_id=_uuid(tenant), user_id=user, documents_read=_documents_read(request)
    )


def establishment_context(request: HTTPConnection, establishment_id: str) -> EstablishmentContext:
    """FastAPI dependency for routes under /establishments/{establishment_id}/: request_context
    plus exactly one establishment header (a non-nil UUID equal to the path's) and one
    jurisdiction header (US, EU or BR). Anything else answers 400: the request did not come
    through the core's establishment route. Only these routes may read the two headers.
    """
    caller = request_context(request)
    establishment = _one(request, ESTABLISHMENT_HEADER)
    jurisdiction = _one(request, JURISDICTION_HEADER)
    if establishment != establishment_id or jurisdiction not in JURISDICTIONS:
        raise HTTPException(status_code=400, detail=_DETAIL)
    return EstablishmentContext(
        tenant_id=caller.tenant_id,
        user_id=caller.user_id,
        documents_read=caller.documents_read,
        establishment_id=_uuid(establishment),
        jurisdiction=jurisdiction,
    )
