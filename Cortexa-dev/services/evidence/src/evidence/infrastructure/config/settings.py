from typing import Literal

from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class EvidenceSettings(BaseSettings):
    uspto_base: str = "https://api.uspto.gov"
    epo_base: str = "https://ops.epo.org"
    epo_token_path: str = "/3.2/auth/accesstoken"
    epo_query_max_terms: int = 12
    epo_query_max_length: int = 4000
    lens_base: str = "https://api.lens.org"
    patent_api_timeout_seconds: float = 15.0
    patent_api_max_retries: int = 3
    uspto_enrich_top_n: int = 3
    epo_enrich_top_n: int = 5
    epo_enrich_max_concurrency: int = 3
    # EPO OPS advertises a per-tenant search throttling ceiling via the
    # x-throttling-control response header (search=green:5, i.e. 5 req/s).
    # epo_search_target_rps keeps headroom under that ceiling; the per-replica
    # rate limiter then divides the target across evidence_max_replicas so
    # concurrent Container App replicas don't collectively exceed the ceiling.
    epo_search_ceiling_rps: float = 5.0
    epo_search_target_rps: float = 4.0
    # Must be kept equal to the evidence Container App's Terraform max_replicas
    # (deploy/environments/*/main.tf) — it is the denominator used to derive
    # epo_search_max_rps_per_replica below.
    evidence_max_replicas: int = 10
    # Explicit, overridable per-replica cap (default = epo_search_target_rps /
    # evidence_max_replicas = 4.0 / 10 = 0.4). Deliberately NOT computed at
    # runtime from the two fields above, so an operator can override this
    # directly via env var if replica count or ceiling changes without having
    # to recompute the derived value.
    epo_search_max_rps_per_replica: float = 0.4
    # USPTO Open Data Portal advertises burst=1 (no parallel requests per key);
    # rate ceiling 4-15 req/s depending on volume. Parallel reads must be avoided.
    # uspto_search_target_rps stays safely under the documented ceiling; the
    # per-replica limiter divides across evidence_max_replicas so concurrent
    # Container App replicas don't collectively exceed the rate.
    uspto_search_ceiling_rps: float = 4.0
    uspto_search_target_rps: float = 3.0
    # Explicit, overridable per-replica cap (default = uspto_search_target_rps /
    # evidence_max_replicas = 3.0 / 10 = 0.3). Deliberately NOT computed at
    # runtime from the two fields above, so an operator can override this
    # directly via env var if replica count or ceiling changes without having
    # to recompute the derived value.
    uspto_search_max_rps_per_replica: float = 0.3
    # BUG147 throttle-retry budget invariant — keep these consistent:
    #   epo_throttle_retry_budget_seconds MUST stay strictly less than
    #   evidence_patent_acquire_timeout_seconds (currently 20s, see below),
    #   so a throttled EPO search can never itself exhaust the patent-branch
    #   acquire timeout.
    #   The whole patent branch — including any EPO throttle backoff — must
    #   stay inside evidence_candidate_deadline_seconds (150s, see BUG140/BUG145
    #   deadline budget invariant below).
    epo_throttle_retry_budget_seconds: float = 8.0
    epo_throttle_max_retries: int = 2
    # USPTO throughput sub-invariant: ensures each candidate can finish within
    # evidence_candidate_deadline_seconds (150s) at fair-share throughput.
    # Formula: (1 + uspto_enrich_top_n * 2) / (uspto_search_max_rps_per_replica /
    # evidence_patent_global_max_concurrency) < evidence_candidate_deadline_seconds
    # With top_n=3 → 7 reqs/candidate; fair-share 0.3/4 = 0.075 req/s;
    # 7 / 0.075 ≈ 93s < 150s. Changes to uspto_enrich_top_n, per-replica rate,
    # or evidence_patent_global_max_concurrency MUST re-check this invariant.
    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1
    keyvault_uri: str = ""
    local_dev: bool = False

    vector_router_url: str
    corpus_search_timeout_seconds: float = 15.0
    corpus_search_top_k: int = 10
    corpus_max_concurrency: int = 5
    corpus_max_retries: int = 3
    corpus_file_path: str = "data/corpus/seed_patents.jsonl"
    corpus_load_batch_size: int = 16
    corpus_load_timeout_seconds: float = 30.0
    corpus_load_max_retries: int = 3

    model_router_url: str
    # Timeout hierarchy invariant (BUG145) — keep these numbers consistent:
    #   model-router per-attempt call timeout (patent_research) = 40s
    #   primary + BUG136 fallback chain worst case               = 40 + 40 = 80s
    #   evidence httpx per-attempt timeout (below)               = 100s, covers the 80s chain
    #   evidence-side retries of /complete (below)               = 0; model-router already owns
    #     retry+fallback — an outer re-run of deadline-exceeded work is the BUG144 loop trigger
    #   acquire headroom (evidence_llm_acquire_timeout_seconds)  = 30s
    #   worst realistic path = 30 (acquire) + 80 (chain) = 110s
    #     < evidence_candidate_deadline_seconds (150s, see BUG140 Phase A2 below)
    #     — 40s margin, comfortably clear of the 120s figure this invariant was originally
    #     derived against before BUG140 raised the deadline.
    llm_research_timeout_seconds: float = 100.0
    llm_research_max_retries: int = 0
    llm_research_total_retry_budget_seconds: float = 400.0
    llm_research_max_concurrency: int = 2
    llm_research_backoff_max_seconds: float = 10.0
    llm_research_honor_retry_after: bool = True
    # Source-3 deep-research model override; None falls back to the batch's primary evidence model.
    llm_research_model: str | None = None

    cosmos_uri: str = ""
    cosmos_database: str = "cortexa-pipeline"
    evidence_container: str = "evidence_bundles"
    candidates_container: str = "candidates"
    batches_container: str = "batches"
    config_container: str = "config"
    batch_terminal_cache_ttl_seconds: float = 30.0
    patent_config_cache_ttl_seconds: float = 30.0
    servicebus_namespace_fqdn: str = ""
    evidence_completed_topic: str = "evidence-completed"
    evidence_failed_topic: str = "evidence-failed"
    evidence_requested_topic: str = "evidence-requested"
    evidence_subscription: str = "evidence"
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0
    consumer_session_lock_renewal_seconds: float = 1800.0
    consumer_max_concurrent_sessions: int = 4
    servicebus_sessions_enabled: bool = True

    # PATENT SUB-SOURCE floor: governs USPTO/EPO/Lens within PatentApi branch only
    min_live_patent_sources: int = 2

    # TOP-LEVEL triangulation floor: governs PatentApi/SeedCorpus/LlmResearch
    minimum_active_sources: int = 2
    minimum_source_policy: Literal["flag", "fail"] = "flag"

    # US111 PART B: Global cross-candidate concurrency limits (per-replica)
    evidence_llm_global_max_concurrency: int = 2
    evidence_patent_global_max_concurrency: int = 4
    evidence_corpus_global_max_concurrency: int = 5
    # Deadline budget invariant (BUG140 Phase A2, re-derived by BUG145):
    #   deadline >= llm_acquire_timeout + (model_router_call_timeout(patent_research) x 2 for
    #   the BUG136 primary+fallback chain) + headroom
    # model_router_patent_research_call_timeout_seconds mirrors the "patent_research"
    # TaskOverrides.CallTimeoutSeconds in services/model-router/src/Api/appsettings.json
    # (currently 40s, lowered from 60s by BUG145) — update this value here if that override
    # ever changes.
    # 30 (acquire, BUG145) + 80 (40s call x 2 for primary+fallback) = 110,
    #   150 - 110 = 40s headroom.
    # BUG145: llm_research_max_retries dropped to 0 (model-router already owns retry+fallback;
    # an evidence-side outer retry of already-deadline-exceeded work was the BUG144 loop
    # trigger) and llm_research_max_concurrency/evidence_llm_global_max_concurrency halved to
    # cut Source-3 (gpt-5.4) saturation under fan-out.
    evidence_candidate_deadline_seconds: float = 150.0
    evidence_llm_acquire_timeout_seconds: float = 30.0
    evidence_patent_acquire_timeout_seconds: float = 20.0
    evidence_corpus_acquire_timeout_seconds: float = 20.0
    model_router_patent_research_call_timeout_seconds: float = 40.0

    # Local-dev only — populated from .env when local_dev=True
    uspto_api_key: str = ""
    epo_consumer_key: str = ""
    epo_oauth_secret: str = ""
    lens_api_key: str = ""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("model_router_url", "vector_router_url", mode="before")
    @classmethod
    def _reject_blank(cls, v: str, info: object) -> str:
        if isinstance(v, str) and not v.strip():
            field_name = getattr(info, "field_name", "field")
            raise ValueError(
                f"{field_name} must not be blank — set the corresponding environment variable"
            )
        return v
