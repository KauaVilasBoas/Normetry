"""Token and cost accounting: one ai.model_usage row per model call, per tenant and user."""

from collections.abc import Mapping
from dataclasses import dataclass
from decimal import Decimal
from uuid import UUID

from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.settings import ModelPrice


@dataclass(frozen=True, slots=True)
class Usage:
    """What one model call used, as the provider reported it."""

    model: str
    input_tokens: int
    output_tokens: int  # thinking included
    cache_creation_input_tokens: int = 0
    cache_read_input_tokens: int = 0

    def cost_usd(self, price: ModelPrice) -> Decimal:
        return (
            self.input_tokens * price.input
            + self.output_tokens * price.output
            + self.cache_creation_input_tokens * price.cache_write
            + self.cache_read_input_tokens * price.cache_read
        ) / 1_000_000


_INSERT = text(
    """
    INSERT INTO ai.model_usage (tenant_id, user_id, model, input_tokens, output_tokens,
                                cache_creation_input_tokens, cache_read_input_tokens, cost_usd)
    VALUES (:tenant_id, :user_id, :model, :input_tokens, :output_tokens,
            :cache_creation_input_tokens, :cache_read_input_tokens, :cost_usd)
    """
)


def record_usage(
    engine: Engine,
    usage: Usage,
    *,
    tenant_id: UUID,
    user_id: str,
    prices: Mapping[str, ModelPrice],
) -> None:
    """Store one call's tokens and cost for the tenant and user from the internal headers.

    Raises KeyError for a model with no price: a cost is never recorded as zero by mistake.
    """
    cost = usage.cost_usd(prices[usage.model])
    with tenant_transaction(engine, tenant_id) as connection:
        connection.execute(
            _INSERT,
            {
                "tenant_id": tenant_id,
                "user_id": user_id,
                "model": usage.model,
                "input_tokens": usage.input_tokens,
                "output_tokens": usage.output_tokens,
                "cache_creation_input_tokens": usage.cache_creation_input_tokens,
                "cache_read_input_tokens": usage.cache_read_input_tokens,
                "cost_usd": cost,
            },
        )
