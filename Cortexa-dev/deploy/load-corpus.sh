#!/usr/bin/env bash
set -euo pipefail

# One-shot operational corpus loader (US107).
#
# The deployed vector-router is internal-only with no VNet, so nothing outside
# the Container Apps cluster can reach it. The api-gateway proxies
# /evidence/corpus/bulk-load, so this script loads the local JSONL dump through
# the gateway (JWT auth) → gateway → evidence service → vector-router (inside
# VNet). This is the only reachable path for corpus loading without a VNet or
# port-forward.
#
# Cost: A filtered BigQuery query (country + date + CPC) scans ~230 GiB even
# for small row limits (BigQuery reads full columns). This is under the 1 TB/month
# always-free tier (~23%) so cost is $0. The Azure embedding + AI Search cost
# scales with record count, not BigQuery scan size.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EVIDENCE_DIR="${SCRIPT_DIR}/../services/evidence"

# Required environment variables
: "${GATEWAY_URL:?GATEWAY_URL environment variable is required}"
: "${LOGIN_EMAIL:?LOGIN_EMAIL environment variable is required}"
: "${LOGIN_PASSWORD:?LOGIN_PASSWORD environment variable is required}"
: "${GCP_PROJECT_ID:?GCP_PROJECT_ID environment variable is required}"
: "${GCP_SERVICE_ACCOUNT_PATH:?GCP_SERVICE_ACCOUNT_PATH environment variable is required}"

# Resolve the SA key path to absolute against the caller's CWD now, before any
# `cd`. The dump step runs from services/evidence, so a relative key path would
# otherwise resolve there and fail with "key file not found".
if [[ ! -f "$GCP_SERVICE_ACCOUNT_PATH" ]]; then
    echo "ERROR: service account key file not found: ${GCP_SERVICE_ACCOUNT_PATH}" >&2
    echo "       (path is resolved from the current directory: $(pwd))" >&2
    exit 1
fi
GCP_SERVICE_ACCOUNT_PATH="$(cd "$(dirname "$GCP_SERVICE_ACCOUNT_PATH")" && pwd)/$(basename "$GCP_SERVICE_ACCOUNT_PATH")"

# Optional BigQuery query bounds
BIGQUERY_ROW_LIMIT="${BIGQUERY_ROW_LIMIT:-1000}"
BIGQUERY_COUNTRY="${BIGQUERY_COUNTRY:-US}"
BIGQUERY_DATE_FROM="${BIGQUERY_DATE_FROM:-0}"
BIGQUERY_DATE_TO="${BIGQUERY_DATE_TO:-0}"
BIGQUERY_CPC_PREFIX="${BIGQUERY_CPC_PREFIX:-}"
BIGQUERY_MAX_BYTES_BILLED="${BIGQUERY_MAX_BYTES_BILLED:-322122547200}"
BATCH_SIZE="${BATCH_SIZE:-100}"
CORPUS_DUMP_PATH="${CORPUS_DUMP_PATH:-data/corpus/bigquery_dump.jsonl}"
FORCE="${FORCE:-0}"

# Optional verification: read the AI Search index document count before and
# after the load to confirm what landed. The search service is AAD-only, so this
# uses an Azure AD token (az CLI) — the running identity needs the
# "Search Index Data Reader" role. Enable with --verify or VERIFY=1.
VERIFY="${VERIFY:-0}"
AI_SEARCH_ENDPOINT="${AI_SEARCH_ENDPOINT:-https://cortexa-dev-search.search.windows.net}"
AI_SEARCH_INDEX_NAME="${AI_SEARCH_INDEX_NAME:-cortexa-corpus}"
SEARCH_API_VERSION="2023-11-01"

AUTH_TOKEN=""
TOTAL_LOADED=0
TOTAL_SKIPPED=0
TOTAL_FAILED=0

log_error() {
    echo "ERROR: $*" >&2
}

log_info() {
    echo "INFO: $*"
}

login() {
    log_info "Authenticating with gateway..."
    local response
    local payload
    payload=$(jq -n --arg email "$LOGIN_EMAIL" --arg password "$LOGIN_PASSWORD" '{email: $email, password: $password}')
    # Pass the payload on stdin (-d @-), not as a CLI arg, so the password never
    # appears in the process list (ps aux) while curl runs.
    response=$(printf '%s' "$payload" | curl -sSf -X POST "${GATEWAY_URL}/auth/login" \
        -H "Content-Type: application/json" \
        -d @- 2>&1 || true)

    if [[ -z "$response" ]]; then
        log_error "Login failed: no response from ${GATEWAY_URL}/auth/login"
        return 1
    fi

    # The gateway wraps responses in a {success, data} envelope, so the token is
    # at .data.access_token; fall back to a bare .access_token for direct calls.
    AUTH_TOKEN=$(echo "$response" | jq -r '.data.access_token // .access_token // empty')
    if [[ -z "$AUTH_TOKEN" ]]; then
        log_error "Login failed: no access_token in response"
        return 1
    fi
    log_info "Login successful"
}

dump_corpus() {
    log_info "Dumping BigQuery corpus (this runs a dry-run cost estimate first)..."
    cd "$EVIDENCE_DIR"

    CORPUS_SOURCE=bigquery \
    VECTOR_ROUTER_URL="${GATEWAY_URL}" \
    GCP_PROJECT_ID="${GCP_PROJECT_ID}" \
    GCP_SERVICE_ACCOUNT_PATH="${GCP_SERVICE_ACCOUNT_PATH}" \
    BIGQUERY_ROW_LIMIT="${BIGQUERY_ROW_LIMIT}" \
    BIGQUERY_COUNTRY="${BIGQUERY_COUNTRY}" \
    BIGQUERY_DATE_FROM="${BIGQUERY_DATE_FROM}" \
    BIGQUERY_DATE_TO="${BIGQUERY_DATE_TO}" \
    BIGQUERY_CPC_PREFIX="${BIGQUERY_CPC_PREFIX}" \
    BIGQUERY_MAX_BYTES_BILLED="${BIGQUERY_MAX_BYTES_BILLED}" \
    CORPUS_DUMP_PATH="${CORPUS_DUMP_PATH}" \
    uv run python -m evidence.scripts.dump_bigquery_corpus

    cd - > /dev/null
}

confirm_upload() {
    local record_count
    record_count=$(wc -l < "${EVIDENCE_DIR}/${CORPUS_DUMP_PATH}")
    log_info "Dump complete: ${record_count} records in ${CORPUS_DUMP_PATH}"

    if [[ "$FORCE" == "1" ]] || [[ -n "${YES:-}" ]]; then
        log_info "Confirmation skipped (FORCE=1 or --yes)"
        return 0
    fi

    read -rp "Upload ${record_count} records to ${GATEWAY_URL}? (y/N): " confirm
    if [[ "${confirm,,}" != "y" ]]; then
        log_error "Upload cancelled by user"
        return 1
    fi
}

upload_batches() {
    local jsonl_path="${EVIDENCE_DIR}/${CORPUS_DUMP_PATH}"
    local total_lines
    total_lines=$(wc -l < "$jsonl_path")
    local batch_num=0
    # Declare the accumulator so `${#batch_lines[@]}` is safe under `set -u`
    # even when the dump produced zero records (empty JSONL).
    local batch_lines=()

    log_info "Uploading ${total_lines} records in batches of ${BATCH_SIZE}..."

    while IFS= read -r line || [[ -n "$line" ]]; do
        batch_lines+=("$line")

        if (( ${#batch_lines[@]} >= BATCH_SIZE )) || [[ -z "$line" && ${#batch_lines[@]} -gt 0 ]]; then
            batch_num=$((batch_num + 1))
            upload_single_batch "$batch_num" "${batch_lines[@]}"
            batch_lines=()
        fi
    done < "$jsonl_path"

    if (( ${#batch_lines[@]} > 0 )); then
        batch_num=$((batch_num + 1))
        upload_single_batch "$batch_num" "${batch_lines[@]}"
    fi

    log_info "Upload complete: ${batch_num} batches processed"
}

upload_single_batch() {
    local batch_num=$1
    shift
    local lines=("$@")

    local body
    body=$(printf '%s\n' "${lines[@]}" | jq -s '{records: .}')

    # Pass the body on stdin (-d @-), not as a CLI arg. A full batch JSON exceeds
    # Linux's 128 KB single-argument limit (MAX_ARG_STRLEN), which would abort
    # curl with "Argument list too long" before any request is sent.
    local response
    response=$(printf '%s' "$body" | curl -sSf -X POST "${GATEWAY_URL}/evidence/corpus/bulk-load" \
        -H "Authorization: Bearer ${AUTH_TOKEN}" \
        -H "Content-Type: application/json" \
        -d @- 2>&1) || {
        log_error "Batch ${batch_num} failed: HTTP error"
        return 1
    }

    local loaded skipped failed
    loaded=$(echo "$response" | jq -r '.loaded_count // 0')
    skipped=$(echo "$response" | jq -r '.skipped_count // 0')
    failed=$(echo "$response" | jq -r '.failed_count // 0')

    TOTAL_LOADED=$((TOTAL_LOADED + loaded))
    TOTAL_SKIPPED=$((TOTAL_SKIPPED + skipped))
    TOTAL_FAILED=$((TOTAL_FAILED + failed))

    log_info "Batch ${batch_num}: loaded=${loaded}, skipped=${skipped}, failed=${failed}"
}

# Prints the current document count of the AI Search index, or "unavailable"
# (never fails the run — verification is best-effort). Requires the az CLI and a
# principal with the Search Index Data Reader role; the index returns 404 until
# the first load creates it, which reports as 0.
index_doc_count() {
    if ! command -v az > /dev/null 2>&1; then
        echo "unavailable"
        return 0
    fi
    local token
    token=$(az account get-access-token --resource https://search.azure.com \
        --query accessToken -o tsv 2>/dev/null || true)
    if [[ -z "$token" ]]; then
        echo "unavailable"
        return 0
    fi
    local url raw status body
    url="${AI_SEARCH_ENDPOINT}/indexes/${AI_SEARCH_INDEX_NAME}/docs/\$count?api-version=${SEARCH_API_VERSION}"
    # Append the HTTP status on its own final line, then split with pure bash
    # parameter expansion — no tail/sed subshells (which could trip pipefail on
    # an empty body, and sed '$d' would wrongly delete a single-line count).
    raw=$(curl -s -w $'\n%{http_code}' --max-time 20 "$url" \
        -H "Authorization: Bearer ${token}" 2>/dev/null || true)
    if [[ -z "$raw" ]]; then
        echo "unavailable"
        return 0
    fi
    status="${raw##*$'\n'}"   # text after the last newline
    body="${raw%$'\n'*}"      # everything before the last newline
    # Collapse any residual whitespace/newlines so the numeric match is clean.
    body="${body//[$'\n\r\t ']/}"
    if [[ "$status" == "404" ]]; then
        echo "0"  # index not created yet
    elif [[ "$status" == "200" && "$body" =~ ^[0-9]+$ ]]; then
        echo "$body"
    else
        echo "unavailable"
    fi
}

main() {
    for arg in "$@"; do
        case "$arg" in
            --yes) YES=1 ;;
            --verify) VERIFY=1 ;;
        esac
    done

    local count_before=""
    if [[ "$VERIFY" == "1" ]]; then
        count_before=$(index_doc_count)
        log_info "Index '${AI_SEARCH_INDEX_NAME}' document count before load: ${count_before}"
    fi

    login
    dump_corpus
    confirm_upload
    upload_batches

    log_info "Final summary: loaded=${TOTAL_LOADED}, skipped=${TOTAL_SKIPPED}, failed=${TOTAL_FAILED}"

    if [[ "$VERIFY" == "1" ]]; then
        local count_after
        count_after=$(index_doc_count)
        log_info "Index '${AI_SEARCH_INDEX_NAME}' document count after load: ${count_after} (was ${count_before})"
        if [[ "$count_after" == "unavailable" ]]; then
            log_info "Verification unavailable (needs az CLI + Search Index Data Reader role); check the portal instead."
        fi
    fi

    if (( TOTAL_FAILED > 0 )); then
        return 1
    fi
}

main "$@"
