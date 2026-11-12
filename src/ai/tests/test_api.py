import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError
from starlette.websockets import WebSocketDisconnect

from normetry_ai.api import create_app
from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER
from normetry_ai.settings import Settings

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
