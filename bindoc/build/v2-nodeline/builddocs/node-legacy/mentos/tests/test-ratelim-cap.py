from __future__ import annotations

from dataclasses import dataclass
from time import perf_counter
from uuid import uuid4

import requests

from api.db.db import db


# Five sequential batches double in size, reaching 31 temporary accounts.
REQUEST_TIMEOUT_SECONDS = 10
SAMPLE_BATCH_SIZES = (1, 2, 4, 8, 16)
HEALTH_PATH = "/db/health"


@dataclass(frozen=True)
# Keep the HTTP outcome and timing together for logging and rollups.
class Result:
    operation: str
    status_code: int | None
    latency_seconds: float
    response_bytes: int
    error: str | None = None


# Send one authenticated CRUD request and return its measured result.
# TODO: Use the /acc account route after its API contract is implemented.
def api_request(
    session: requests.Session,
    operation: str,
    action: str,
    *,
    payload: dict[str, object] | None = None,
    record_id: int | None = None,
) -> tuple[Result, dict[str, object] | None]:
    path = "/db/users"
    method = "POST"
    if action == "update":
        if record_id is None:
            raise ValueError("record_id is required for update.")
        path = f"{path}/{record_id}"
        method = "PATCH"
    elif action == "delete":
        if record_id is None:
            raise ValueError("record_id is required for delete.")
        path = f"{path}/{record_id}"
        method = "DELETE"

    # Restrict the short-lived token to the users resource and this action.
    token = db._issue_token(
        "db:write",
        "users",
        {"create": "create", "update": "update", "delete": "delete"}[action],
    )
    headers = {"Accept": "application/json", "Authorization": f"Bearer {token}"}
    started_at = perf_counter()
    try:
        response = session.request(
            method,
            f"{db.host}{path}",
            headers=headers,
            json=payload,
            timeout=REQUEST_TIMEOUT_SECONDS,
        )
        elapsed = perf_counter() - started_at
        result = Result(operation, response.status_code, elapsed, len(response.content))
        # Preserve non-success statuses such as 429 for the test report.
        if not response.ok:
            return result, None
        if action == "delete":
            return result, None
        body = response.json()
        data = body.get("data") if isinstance(body, dict) else None
        return result, data if isinstance(data, dict) else None
    except requests.RequestException as error:
        elapsed = perf_counter() - started_at
        return Result(operation, None, elapsed, 0, str(error)), None


# Log an individual request so failures and slow samples remain identifiable.
def log_result(
    result: Result,
    *,
    sample_number: int,
    batch_size: int,
    request_number: int,
) -> None:
    status = str(result.status_code) if result.status_code is not None else "NETWORK_ERROR"
    speed = result.response_bytes / result.latency_seconds if result.latency_seconds else 0
    print(
        f"LOG operation={result.operation} sample={sample_number}/"
        f"{len(SAMPLE_BATCH_SIZES)} batch_size={batch_size} "
        f"request={request_number}/{batch_size} status={status} "
        f"latency_ms={result.latency_seconds * 1000:.2f} "
        f"response_bytes={result.response_bytes} response_speed={speed:.2f} B/s"
        + (f" error={result.error}" if result.error else ""),
        flush=True,
    )


# Summarize success rate, observed 429s, latency, and sequential throughput.
def summarize(operation: str, results: list[Result]) -> None:
    successful = [result for result in results if result.status_code is not None and 200 <= result.status_code < 300]
    rate_limited = [result for result in results if result.status_code == 429]
    total_latency = sum(result.latency_seconds for result in results)
    average_latency = total_latency / len(results) * 1000 if results else 0
    throughput = len(results) / total_latency if total_latency else 0
    print(
        f"SUMMARY operation={operation} growth_samples={len(SAMPLE_BATCH_SIZES)} "
        f"requests={len(results)} "
        f"success={len(successful)} rate_limited_429={len(rate_limited)} "
        f"avg_latency_ms={average_latency:.2f} "
        f"request_rate={throughput:.2f} requests/s",
        flush=True,
    )


# Check whether the database-backed API still responds after test traffic.
def check_database_life() -> bool:
    started_at = perf_counter()
    try:
        response = requests.get(
            f"{db.host}{HEALTH_PATH}",
            timeout=REQUEST_TIMEOUT_SECONDS,
        )
        elapsed = perf_counter() - started_at
        alive = response.ok
        print(
            f"DB_LIFECHECK status={response.status_code} alive={alive} "
            f"latency_ms={elapsed * 1000:.2f} response={response.text}",
            flush=True,
        )
        return alive
    except requests.RequestException as error:
        elapsed = perf_counter() - started_at
        print(
            f"DB_LIFECHECK status=NETWORK_ERROR alive=False "
            f"latency_ms={elapsed * 1000:.2f} error={error}",
            flush=True,
        )
        return False


# Create growing test batches, update them, delete them, then check health.
def main() -> None:
    if not db.host:
        raise RuntimeError("Set DBSERV_URL in api/.env before running endpoint tests.")

    # Track created IDs so finally can clean up even after a request fails.
    run_id = uuid4().hex[:12]
    session = requests.Session()
    records_by_sample: dict[int, list[int]] = {}
    pending_deletions: set[int] = set()
    operation_results = {"create": [], "update": [], "delete": []}
    failures: list[str] = []

    print(
        f"ACCOUNT RATE-LIMIT/CAPACITY TEST base_url={db.host} "
        f"run_id={run_id} sample_batch_sizes={SAMPLE_BATCH_SIZES}",
        flush=True,
    )
    print(
        "This is a sequential, bounded DB API test (31 account records maximum); "
        "the /acc account-creation route is currently a 501 skeleton.",
        flush=True,
    )

    try:
        # Create 1, 2, 4, 8, and 16 distinct users in separate samples.
        for sample_number, batch_size in enumerate(SAMPLE_BATCH_SIZES, start=1):
            batch_ids: list[int] = []
            records_by_sample[sample_number] = batch_ids
            print(
                f"SAMPLE_BEGIN operation=create sample={sample_number}/5 "
                f"batch_size={batch_size}",
                flush=True,
            )
            for request_number in range(1, batch_size + 1):
                account_number = sum(SAMPLE_BATCH_SIZES[: sample_number - 1]) + request_number
                result, data = api_request(
                    session,
                    "create",
                    "create",
                    payload={
                        "external_subject": f"demo-load-{run_id}-{account_number}",
                        "email": f"demo-load-{run_id}-{account_number}@example.invalid",
                        "display_name": f"Load Test {run_id} {account_number}",
                        "account_status": "pending",
                    },
                )
                operation_results["create"].append(result)
                log_result(
                    result,
                    sample_number=sample_number,
                    batch_size=batch_size,
                    request_number=request_number,
                )
                if result.status_code == 429:
                    print("RATE_LIMIT observed: server returned HTTP 429.", flush=True)
                if result.status_code is None or not 200 <= result.status_code < 300:
                    failures.append(f"create sample {sample_number} request {request_number}: HTTP {result.status_code} {result.error or ''}")
                    continue
                record_id = data.get("id") if data else None
                if not isinstance(record_id, int):
                    failures.append(f"create sample {sample_number} request {request_number}: response did not contain an integer id")
                    continue
                batch_ids.append(record_id)
                pending_deletions.add(record_id)
                print(
                    f"CREATED sample={sample_number} request={request_number} "
                    f"user_id={record_id}",
                    flush=True,
                )

        summarize("create", operation_results["create"])

        # Update the same records, isolating update behavior from creation.
        for sample_number, batch_size in enumerate(SAMPLE_BATCH_SIZES, start=1):
            batch_ids = records_by_sample[sample_number]
            print(
                f"SAMPLE_BEGIN operation=update sample={sample_number}/5 "
                f"planned_batch_size={batch_size} actual_batch_size={len(batch_ids)}",
                flush=True,
            )
            for request_number, record_id in enumerate(batch_ids, start=1):
                result, _ = api_request(
                    session,
                    "update",
                    "update",
                    record_id=record_id,
                    payload={"display_name": f"Updated Load Test {run_id} {record_id}"},
                )
                operation_results["update"].append(result)
                log_result(
                    result,
                    sample_number=sample_number,
                    batch_size=len(batch_ids),
                    request_number=request_number,
                )
                if result.status_code == 429:
                    print("RATE_LIMIT observed: server returned HTTP 429.", flush=True)
                if result.status_code is None or not 200 <= result.status_code < 300:
                    failures.append(f"update user {record_id}: HTTP {result.status_code} {result.error or ''}")

        summarize("update", operation_results["update"])

        # Delete created records and stop tracking each successful deletion.
        for sample_number, batch_size in enumerate(SAMPLE_BATCH_SIZES, start=1):
            batch_ids = records_by_sample[sample_number]
            print(
                f"SAMPLE_BEGIN operation=delete sample={sample_number}/5 "
                f"planned_batch_size={batch_size} actual_batch_size={len(batch_ids)}",
                flush=True,
            )
            for request_number, record_id in enumerate(batch_ids, start=1):
                result, _ = api_request(
                    session,
                    "delete",
                    "delete",
                    record_id=record_id,
                )
                operation_results["delete"].append(result)
                log_result(
                    result,
                    sample_number=sample_number,
                    batch_size=len(batch_ids),
                    request_number=request_number,
                )
                if result.status_code == 429:
                    print("RATE_LIMIT observed: server returned HTTP 429.", flush=True)
                if result.status_code is not None and 200 <= result.status_code < 300:
                    pending_deletions.discard(record_id)
                else:
                    failures.append(f"delete user {record_id}: HTTP {result.status_code} {result.error or ''}")

        summarize("delete", operation_results["delete"])
    finally:
        # Retry cleanup for IDs that remained after normal deletion attempts.
        for record_id in sorted(pending_deletions):
            cleanup, _ = api_request(
                session,
                "delete",
                "delete",
                record_id=record_id,
            )
            log_result(
                cleanup,
                sample_number=0,
                batch_size=len(pending_deletions),
                request_number=record_id,
            )
            if cleanup.status_code is not None and 200 <= cleanup.status_code < 300:
                print(f"CLEANUP deleted remaining test user_id={record_id}", flush=True)
            else:
                failures.append(
                    f"cleanup user {record_id}: HTTP {cleanup.status_code} {cleanup.error or ''}"
                )
        session.close()
        database_alive = check_database_life()

    # No server-side limiter is configured, so 429 is reported but not required.
    # TODO: Assert the expected limit once its policy and threshold are defined.
    if not any(result.status_code == 429 for results in operation_results.values() for result in results):
        print("RATE_LIMIT_RESULT no HTTP 429 was observed; no rate-limit threshold was reached.", flush=True)
    if not database_alive:
        failures.append("database health check failed after the test")
    if failures:
        details = "\n".join(f"- {failure}" for failure in failures)
        raise RuntimeError(f"Capacity test had {len(failures)} failure(s):\n{details}")


if __name__ == "__main__":
    main()
