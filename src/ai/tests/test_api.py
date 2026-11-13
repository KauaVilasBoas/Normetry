from typing import Annotated

import pytest
from fastapi import Depends
from fastapi.testclient import TestClient
from pydantic import ValidationError
from starlette.websockets import WebSocketDisconnect

from normetry_ai.api import create_app
from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER
from normetry_ai.api.context import (
    ESTABLISHMENT_HEADER,
    JURISDICTION_HEADER,
    TENANT_HEADER,
    USER_HEADER,
    EstablishmentContext,
    RequestContext,
    establishment_context,
    request_context,
)
from normetry_ai.settings import MigrationSettings, Settings

TOKEN = "test-internal-token-0123456789abcdef"
AUTH = {INTERNAL_TOKEN_HEADER: TOKEN}


@pytest.fixture
def client() -> TestClient:
    return TestClient(create_app(Settings(internal_token=TOKEN, _env_file=None)))


def test_health_with_valid_token_returns_200(client: TestClient) -> None:
    response = client.get("/health", headers=AUTH)

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


@pytest.mark.parametrize(
    "headers",
    [
        {},
        {INTERNAL_TOKEN_HEADER: "wrong-internal-token-0123456789abcdef"},
        {INTERNAL_TOKEN_HEADER: ""},
        [(INTERNAL_TOKEN_HEADER, TOKEN), (INTERNAL_TOKEN_HEADER, TOKEN)],
    ],
    ids=["missing", "wrong", "empty", "twice"],
)
def test_health_without_exactly_one_valid_token_returns_401(
    client: TestClient, headers: dict[str, str] | list[tuple[str, str]]
) -> None:
    response = client.get("/health", headers=headers)

    assert response.status_code == 401
    assert response.json() == {"detail": "Missing or invalid internal token."}


def test_token_is_checked_before_routing(client: TestClient) -> None:
    assert client.get("/no-such-path").status_code == 401
    assert client.post("/health").status_code == 401
    assert client.get("/no-such-path", headers=AUTH).status_code == 404


def test_websocket_without_token_is_closed(client: TestClient) -> None:
    with pytest.raises(WebSocketDisconnect) as closed, client.websocket_connect("/ws"):
        pass

    assert closed.value.code == 1008


@pytest.mark.parametrize("path", ["/docs", "/redoc", "/openapi.json"])
def test_docs_are_not_served(client: TestClient, path: str) -> None:
    assert client.get(path, headers=AUTH).status_code == 404


@pytest.mark.parametrize("token", [None, "too-short-token"], ids=["missing", "too-short"])
def test_app_refuses_to_start_without_a_valid_token(
    token: str | None, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setitem(Settings.model_config, "env_file", None)  # ignore a developer's .env
    if token is None:
        monkeypatch.delenv("NORMETRY_INTERNAL_TOKEN", raising=False)
    else:
        monkeypatch.setenv("NORMETRY_INTERNAL_TOKEN", token)

    with pytest.raises(ValidationError) as error:
        create_app()

    if token is not None:
        assert token not in str(error.value)


def test_runtime_settings_never_carry_the_migrations_url() -> None:
    assert "migrations_database_url" not in Settings.model_fields
    # The owner URL comes only from the environment, never from src/ai/.env.
    assert MigrationSettings.model_config["env_file"] is None


def test_app_starts_without_a_database() -> None:
    settings = Settings(internal_token=TOKEN, database_url=None, _env_file=None)
    with TestClient(create_app(settings)) as client:  # runs the lifespan
        assert client.get("/health", headers=AUTH).status_code == 200


def _context_client() -> TestClient:
    app = create_app(Settings(internal_token=TOKEN, _env_file=None))

    # Test-only route: the service has no endpoint that just echoes its caller.
    @app.get("/context")
    def context(ctx: Annotated[RequestContext, Depends(request_context)]) -> dict[str, str]:
        return {"tenant_id": str(ctx.tenant_id), "user_id": ctx.user_id}

    return TestClient(app)


TENANT = "11111111-1111-4111-8111-111111111111"
USER = "0b5a4c8e-7f1d-4e2a-9c3b-6d8e1f2a3b4c"


def test_context_headers_without_the_token_get_401() -> None:
    response = _context_client().get("/context", headers={TENANT_HEADER: TENANT, USER_HEADER: USER})

    assert response.status_code == 401


def test_context_carries_the_internal_header_values() -> None:
    response = _context_client().get(
        "/context", headers={**AUTH, TENANT_HEADER: TENANT, USER_HEADER: USER}
    )

    assert response.status_code == 200
    assert response.json() == {"tenant_id": TENANT, "user_id": USER}


@pytest.mark.parametrize(
    "headers",
    [
        [(USER_HEADER, USER)],
        [(TENANT_HEADER, TENANT)],
        [(TENANT_HEADER, TENANT), (TENANT_HEADER, TENANT), (USER_HEADER, USER)],
        [(TENANT_HEADER, TENANT), (USER_HEADER, USER), (USER_HEADER, USER)],
        [(TENANT_HEADER, "not-a-uuid"), (USER_HEADER, USER)],
        [(TENANT_HEADER, "00000000-0000-0000-0000-000000000000"), (USER_HEADER, USER)],
        [(TENANT_HEADER, TENANT.replace("-", "")), (USER_HEADER, USER)],
        [(TENANT_HEADER, TENANT.upper().replace("1", "A")), (USER_HEADER, USER)],
        [(TENANT_HEADER, TENANT), (USER_HEADER, "")],
    ],
    ids=[
        "no-tenant",
        "no-user",
        "two-tenants",
        "two-users",
        "bad-uuid",
        "nil",
        "no-dashes",
        "upper-case",
        "empty-user",
    ],
)
def test_context_without_exactly_one_valid_value_each_gets_400(
    headers: list[tuple[str, str]],
) -> None:
    response = _context_client().get("/context", headers=[*AUTH.items(), *headers])

    assert response.status_code == 400
    assert response.json() == {"detail": "Missing or invalid internal context headers."}


ESTABLISHMENT = "2b9c1d4e-5f6a-4b7c-8d9e-0f1a2b3c4d5e"
OTHER_ESTABLISHMENT = "33333333-3333-4333-8333-333333333333"
CALLER = [*AUTH.items(), (TENANT_HEADER, TENANT), (USER_HEADER, USER)]


def _establishment_client() -> TestClient:
    app = create_app(Settings(internal_token=TOKEN, _env_file=None))

    # Test-only route, shaped like the chat routes under /establishments/{establishment_id}/.
    @app.get("/establishments/{establishment_id}/context")
    def context(
        ctx: Annotated[EstablishmentContext, Depends(establishment_context)],
    ) -> dict[str, str]:
        return {"establishment_id": str(ctx.establishment_id), "jurisdiction": ctx.jurisdiction}

    return TestClient(app)


def test_establishment_context_carries_the_establishment_headers() -> None:
    response = _establishment_client().get(
        f"/establishments/{ESTABLISHMENT}/context",
        headers=[*CALLER, (ESTABLISHMENT_HEADER, ESTABLISHMENT), (JURISDICTION_HEADER, "BR")],
    )

    assert response.status_code == 200
    assert response.json() == {"establishment_id": ESTABLISHMENT, "jurisdiction": "BR"}


@pytest.mark.parametrize(
    ("path", "headers"),
    [
        (ESTABLISHMENT, [(JURISDICTION_HEADER, "BR")]),
        (ESTABLISHMENT, [(ESTABLISHMENT_HEADER, ESTABLISHMENT)]),
        (
            ESTABLISHMENT,
            [
                (ESTABLISHMENT_HEADER, ESTABLISHMENT),
                (ESTABLISHMENT_HEADER, ESTABLISHMENT),
                (JURISDICTION_HEADER, "BR"),
            ],
        ),
        (
            ESTABLISHMENT,
            [
                (ESTABLISHMENT_HEADER, ESTABLISHMENT),
                (JURISDICTION_HEADER, "BR"),
                (JURISDICTION_HEADER, "BR"),
            ],
        ),
        (ESTABLISHMENT, [(ESTABLISHMENT_HEADER, OTHER_ESTABLISHMENT), (JURISDICTION_HEADER, "BR")]),
        (
            ESTABLISHMENT.upper(),
            [(ESTABLISHMENT_HEADER, ESTABLISHMENT.upper()), (JURISDICTION_HEADER, "BR")],
        ),
        (
            "00000000-0000-0000-0000-000000000000",
            [
                (ESTABLISHMENT_HEADER, "00000000-0000-0000-0000-000000000000"),
                (JURISDICTION_HEADER, "BR"),
            ],
        ),
        ("not-a-uuid", [(ESTABLISHMENT_HEADER, "not-a-uuid"), (JURISDICTION_HEADER, "BR")]),
        (ESTABLISHMENT, [(ESTABLISHMENT_HEADER, ESTABLISHMENT), (JURISDICTION_HEADER, "br")]),
        (ESTABLISHMENT, [(ESTABLISHMENT_HEADER, ESTABLISHMENT), (JURISDICTION_HEADER, "AR")]),
        (ESTABLISHMENT, [(ESTABLISHMENT_HEADER, ESTABLISHMENT), (JURISDICTION_HEADER, "")]),
    ],
    ids=[
        "no-establishment",
        "no-jurisdiction",
        "two-establishments",
        "two-jurisdictions",
        "not-the-path",
        "upper-case",
        "nil",
        "bad-uuid",
        "lower-case-jurisdiction",
        "unknown-jurisdiction",
        "empty-jurisdiction",
    ],
)
def test_establishment_routes_without_valid_matching_headers_get_400(
    path: str, headers: list[tuple[str, str]]
) -> None:
    response = _establishment_client().get(
        f"/establishments/{path}/context", headers=[*CALLER, *headers]
    )

    assert response.status_code == 400
    assert response.json() == {"detail": "Missing or invalid internal context headers."}


def test_establishment_routes_still_need_the_caller_headers() -> None:
    headers = [*AUTH.items(), (ESTABLISHMENT_HEADER, ESTABLISHMENT), (JURISDICTION_HEADER, "BR")]

    response = _establishment_client().get(
        f"/establishments/{ESTABLISHMENT}/context", headers=headers
    )

    assert response.status_code == 400
