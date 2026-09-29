#!/usr/bin/env bash
# drain-dlq.sh — Peek/classify or purge Service Bus dead-letter queues.
# See .notes/informations/Development_Notes/DLQ_RUNBOOK.md for operator runbook.
# Usage:
#   ./drain-dlq.sh [--resource-group RG] [--namespace NS] [--purge] [--force]
set -euo pipefail

RESOURCE_GROUP="cortexa-dev-rg"
NAMESPACE="cortexa-dev-bus"
PURGE=false
FORCE=false

# Every consumer subscription that can dead-letter must be covered — a hardcoded subset
# silently hides a real backlog (BUG156: seeding-requested was once omitted). This spans
# the *-requested stage consumers plus the US115 Deep Seeding embedding fan-out
# (asset-embedding-requested → seeding, asset-embedding-completed → orchestrator),
# the US116 Deep Seeding digest fan-out
# (digest-requested → seeding, digest-completed → orchestrator),
# the US117 Deep Seeding landscape fan-out
# (landscape-requested → seeding, landscape-completed → orchestrator),
# and the US119 Deep Seeding grounded ideation loop
# (ideation-completed → orchestrator, seeding-report-requested → seeding).
DLQ_TOPICS=("ingestion-requested" "extraction-requested" "evidence-requested" "scoring-requested" "harvesting-requested" "seeding-requested" "asset-embedding-requested" "asset-embedding-completed" "digest-requested" "digest-completed" "landscape-requested" "landscape-completed" "ideation-completed" "seeding-report-requested")
DLQ_SUBS=("ingestion" "extraction" "evidence" "scoring" "harvesting" "seeding" "seeding" "orchestrator" "seeding" "orchestrator" "seeding" "orchestrator" "orchestrator" "seeding")
API_VERSION="2017-04"

# classify/purge run inside $(...) command substitution, so a plain variable set in
# them cannot reach the parent shell. Record data-plane failures in a temp flag file
# instead, so the script can exit non-zero rather than report a false "all clear".
ERROR_FLAG_FILE="$(mktemp)"
trap 'rm -f "$ERROR_FLAG_FILE"' EXIT

mark_error() { echo 1 >>"$ERROR_FLAG_FILE"; }
had_error()  { [[ -s "$ERROR_FLAG_FILE" ]]; }

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --resource-group) RESOURCE_GROUP="$2"; shift 2 ;;
            --namespace)      NAMESPACE="$2";      shift 2 ;;
            --purge)          PURGE=true;           shift   ;;
            --force)          FORCE=true;           shift   ;;
            *) echo "Unknown argument: $1" >&2; exit 1 ;;
        esac
    done
}

check_dependencies() {
    for cmd in az curl jq; do
        if ! command -v "$cmd" &>/dev/null; then
            echo "ERROR: '$cmd' is required but not found." >&2
            exit 1
        fi
    done
}

get_servicebus_token() {
    local token
    token=$(az account get-access-token --resource "https://servicebus.azure.net/" \
        --query accessToken -o tsv 2>/dev/null)
    if [[ -z "$token" ]]; then
        echo "ERROR: Failed to acquire Service Bus token. Run 'az login' first." >&2
        exit 1
    fi
    echo "$token"
}

get_dlq_count() {
    local rg="$1" ns="$2" topic="$3" sub="$4"
    az servicebus topic subscription show \
        --resource-group "$rg" \
        --namespace-name "$ns" \
        --topic-name "$topic" \
        --name "$sub" \
        --query "countDetails.deadLetterMessageCount" -o tsv 2>/dev/null || echo "0"
}

dlq_entity_path() {
    local topic="$1" sub="$2"
    echo "${topic}/subscriptions/${sub}/\$deadletterqueue"
}

peek_lock_message() {
    local entity_path="$1" token="$2"
    local url="https://${NAMESPACE}.servicebus.windows.net/${entity_path}/messages/head?timeout=30&api-version=${API_VERSION}"
    curl -si -X POST "$url" -H "Authorization: Bearer $token" -H "Content-Length: 0" 2>/dev/null
}

receive_and_delete_message() {
    local entity_path="$1" token="$2"
    local url="https://${NAMESPACE}.servicebus.windows.net/${entity_path}/messages/head?api-version=${API_VERSION}"
    curl -so /dev/null -w "%{http_code}" -X DELETE "$url" -H "Authorization: Bearer $token" 2>/dev/null
}

abandon_message() {
    local entity_path="$1" token="$2" sequence_number="$3" lock_token="$4"
    local url="https://${NAMESPACE}.servicebus.windows.net/${entity_path}/messages/${sequence_number}/${lock_token}?api-version=${API_VERSION}"
    curl -so /dev/null -w "%{http_code}" -X PUT "$url" -H "Authorization: Bearer $token" -H "Content-Length: 0" 2>/dev/null
}

extract_http_status() {
    echo "$1" | grep -m1 "^HTTP" | awk '{print $2}'
}

extract_broker_property() {
    local response="$1" property="$2"
    local bp_json
    bp_json=$(echo "$response" | grep -i "^BrokerProperties:" | head -1 | sed 's/^[^:]*: //' | tr -d '\r')
    if [[ -z "$bp_json" ]]; then
        echo "(none)"
        return
    fi
    echo "$bp_json" | jq -r --arg p "$property" '.[$p] // "(none)"' 2>/dev/null || echo "(none)"
}

# The dead-letter reason is a standalone HTTP response header, not a field inside
# the BrokerProperties JSON, so it needs its own extractor.
extract_response_header() {
    local response="$1" header="$2" value
    value=$(echo "$response" | grep -i "^${header}:" | head -1 | sed 's/^[^:]*: //' | tr -d '\r"')
    echo "${value:-(none)}"
}

classify_and_release() {
    local entity_path="$1" token="$2" response="$3"
    local -n _counts="$4"
    local reason lock_token seq_num abandon_status

    reason=$(extract_response_header "$response" "DeadLetterReason")
    _counts["$reason"]=$(( ${_counts["$reason"]:-0} + 1 ))

    lock_token=$(extract_broker_property "$response" "LockToken")
    seq_num=$(extract_broker_property "$response" "SequenceNumber")
    [[ "$lock_token" != "(none)" && "$seq_num" != "(none)" ]] || return 0

    abandon_status=$(abandon_message "$entity_path" "$token" "$seq_num" "$lock_token")
    if [[ "$abandon_status" != "200" ]]; then
        printf "  WARN: Failed to abandon message (seq=%s, HTTP %s)\n" "$seq_num" "$abandon_status" >&2
    fi
}

classify_dlq() {
    local topic="$1" sub="$2" token="$3"
    local entity_path dlq_count peeked
    declare -A reason_counts

    entity_path=$(dlq_entity_path "$topic" "$sub")
    dlq_count=$(get_dlq_count "$RESOURCE_GROUP" "$NAMESPACE" "$topic" "$sub")
    dlq_count="${dlq_count//[[:space:]]/}"
    dlq_count="${dlq_count:-0}"
    peeked=0

    while [[ $peeked -lt $dlq_count ]]; do
        local response http_code
        response=$(peek_lock_message "$entity_path" "$token")
        http_code=$(extract_http_status "$response")
        # Service Bus peek-lock returns 201 Created (not 200) on success.
        if [[ "$http_code" == "200" || "$http_code" == "201" ]]; then
            classify_and_release "$entity_path" "$token" "$response" reason_counts
            (( peeked++ )) || true
            continue
        fi
        # 204 = queue drained early (count is a snapshot, so a race is benign).
        # Any other status — most commonly 401/403 for a missing data-plane role —
        # must not be swallowed: it would report a false "0 peeked / all clear".
        if [[ "$http_code" != "204" ]]; then
            printf "  ERROR: peek on %s failed with HTTP %s (expected data-plane access; see DLQ_RUNBOOK.md prerequisites)\n" \
                "$entity_path" "${http_code:-<none>}" >&2
            mark_error
        fi
        break
    done

    echo "TOPIC:${topic}"
    echo "SUB:${sub}"
    echo "COUNT:${dlq_count}"
    echo "PEEKED:${peeked}"
    for key in "${!reason_counts[@]}"; do
        echo "REASON:${key}:${reason_counts[$key]}"
    done
    echo "---"
}

print_classification_table() {
    local data="$1"
    local divider
    divider=$(printf '=%.0s' {1..72})

    echo ""
    echo "Dead-Letter Queue Classification"
    echo "$divider"

    while IFS= read -r line; do
        case "$line" in
            TOPIC:*)  echo "";    printf "  %-18s %s\n" "Topic:"        "${line#TOPIC:}" ;;
            SUB:*)    printf "  %-18s %s\n" "Subscription:" "${line#SUB:}" ;;
            COUNT:*)  local cnt="${line#COUNT:}" ;;
            PEEKED:*) printf "  %-18s %s  (peeked: %s)\n" "DLQ count:" "$cnt" "${line#PEEKED:}"
                      echo "  Reasons:" ;;
            REASON:*) local rest="${line#REASON:}"
                      local reason="${rest%%:*}" cnt2="${rest##*:}"
                      printf "    %-50s %5s\n" "$reason" "$cnt2" ;;
            ---) ;;
        esac
    done <<< "$data"

    echo ""
    echo "$divider"
    echo ""
}

purge_dlq_target() {
    local topic="$1" sub="$2" token="$3"
    local entity_path deleted http_code

    entity_path=$(dlq_entity_path "$topic" "$sub")
    deleted=0

    printf "  Purging %s/%s ..." "$topic" "$sub" >&2

    while true; do
        http_code=$(receive_and_delete_message "$entity_path" "$token")
        if [[ "$http_code" == "200" ]]; then
            (( deleted++ )) || true
            (( deleted % 25 == 0 )) && printf "." >&2
            continue
        fi
        # 204 = queue empty (normal stop). Anything else (e.g. 401/403 for a missing
        # data-plane role) is an error that must be surfaced, not reported as success.
        if [[ "$http_code" != "204" ]]; then
            printf " [HTTP %s — aborting target]" "${http_code:-<none>}" >&2
            mark_error
        fi
        break
    done

    printf " deleted: %d\n" "$deleted" >&2
    echo "$deleted"
}

confirm_purge() {
    echo ""
    echo "WARNING: This will permanently delete all dead-letter messages." >&2
    echo "Namespace: $NAMESPACE" >&2
    for i in "${!DLQ_TOPICS[@]}"; do
        echo "  ${DLQ_TOPICS[$i]}/${DLQ_SUBS[$i]}" >&2
    done
    echo ""
    read -r -p "Type YES to confirm: " answer
    [[ "$answer" == "YES" ]]
}

# ── Entry point ───────────────────────────────────────────────────────────────

parse_args "$@"
check_dependencies

echo "Acquiring Service Bus token ..."
TOKEN=$(get_servicebus_token)

echo "Classifying DLQs in namespace: $NAMESPACE"
ALL_DATA=""
for i in "${!DLQ_TOPICS[@]}"; do
    topic="${DLQ_TOPICS[$i]}"
    sub="${DLQ_SUBS[$i]}"
    echo "  Peeking ${topic}/${sub} ..."
    ALL_DATA+=$(classify_dlq "$topic" "$sub" "$TOKEN")
    ALL_DATA+=$'\n'
done

print_classification_table "$ALL_DATA"

if [[ "$PURGE" != "true" ]]; then
    if had_error; then
        echo "ERROR: one or more queues could not be read — classification above is INCOMPLETE." >&2
        echo "Grant 'Azure Service Bus Data Receiver' on the namespace (see DLQ_RUNBOOK.md) and retry." >&2
        exit 1
    fi
    echo "Run with --purge to drain all dead-letter messages."
    exit 0
fi

if [[ "$FORCE" != "true" ]] && ! confirm_purge; then
    echo "Purge cancelled."
    exit 0
fi

echo "Purging dead-letter queues ..."
TOTAL_DELETED=0
for i in "${!DLQ_TOPICS[@]}"; do
    deleted=$(purge_dlq_target "${DLQ_TOPICS[$i]}" "${DLQ_SUBS[$i]}" "$TOKEN")
    TOTAL_DELETED=$(( TOTAL_DELETED + deleted ))
done

echo ""
echo "Purge complete. Total messages deleted: $TOTAL_DELETED"

if had_error; then
    echo "ERROR: one or more targets aborted before draining — purge is INCOMPLETE." >&2
    echo "Grant 'Azure Service Bus Data Receiver' on the namespace (see DLQ_RUNBOOK.md) and retry." >&2
    exit 1
fi
