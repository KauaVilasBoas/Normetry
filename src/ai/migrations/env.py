"""Alembic environment for schema `ai`, the only schema this service writes.

The `ai` schema and `ai.chunks` arrive in #13. Alembic does not create the schema that holds its
version table, so the first migration run needs `ai` to exist (#13 decides who creates it).
Migrations connect as the owner role (NORMETRY_MIGRATIONS_DATABASE_URL), never as app_ai.
Tenant-owned tables follow the `rls-table` skill.
"""

from logging.config import fileConfig

from alembic import context
from sqlalchemy import create_engine, pool

from normetry_ai.settings import MigrationSettings

config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# Models arrive with ai.chunks (#13); autogenerate needs their MetaData here.
target_metadata = None

SCHEMA = "ai"


def include_name(name: str | None, type_: str, parent_names: dict[str, str | None]) -> bool:
    # Autogenerate only ever sees schema `ai`: never core.* or public.
    return name == SCHEMA if type_ == "schema" else True


def database_url() -> str:
    # The owner role only. No fallback to NORMETRY_DATABASE_URL: the app role never runs DDL.
    url = MigrationSettings().migrations_database_url
    if url is None:
        raise RuntimeError("Set NORMETRY_MIGRATIONS_DATABASE_URL (owner role) to run migrations.")
    return url.get_secret_value()


def configure(**kwargs: object) -> None:
    context.configure(
        target_metadata=target_metadata,
        version_table_schema=SCHEMA,
        include_schemas=True,
        include_name=include_name,
        **kwargs,
    )


def run_migrations_offline() -> None:
    configure(url=database_url(), literal_binds=True, dialect_opts={"paramstyle": "named"})
    with context.begin_transaction():
        context.run_migrations()


def run_migrations_online() -> None:
    engine = create_engine(database_url(), poolclass=pool.NullPool)
    with engine.connect() as connection:
        configure(connection=connection)
        with context.begin_transaction():
            context.run_migrations()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
