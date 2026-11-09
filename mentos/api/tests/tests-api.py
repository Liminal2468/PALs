from __future__ import annotations

from dataclasses import dataclass
from time import perf_counter

import requests

from api.db.db import db


# Benchmark settings cover each mounted service's health route and DB JWKS.
SAMPLES_PER_ENDPOINT = 5
REQUEST_TIMEOUT_SECONDS = 10
ENDPOINTS = (
    "/db/health",
    "/db/.well-known/jwks.json",
    "/acc/health",
    "/sched/health",
    "/fb/health",
    "/adata/health",
    "/connect/health",
    "/auth/v1/health",
    "/chat/v1/health",
)


@dataclass(frozen=True)
# Keep one immutable record per HTTP sample for reliable averaging.
class Sample:
    latency_seconds: float
    response_bytes: int
    status_code: int

    @property
    def bytes_per_second(self) -> float:
        # This is end-to-end response-body speed, not raw network bandwidth.
        return self.response_bytes / self.latency_seconds


# Reuse the session so samples can benefit from persistent HTTPS connections.
def sample_endpoint(
    session: requests.Session,
    base_url: str,
    endpoint: str,
) -> Sample:
    started_at = perf_counter()
    response = session.get(
        f"{base_url}{endpoint}",
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    elapsed = perf_counter() - started_at
    # Fail the sample on HTTP errors instead of averaging an error response.
    response.raise_for_status()
    return Sample(
        latency_seconds=elapsed,
        response_bytes=len(response.content),
        status_code=response.status_code,
    )


# Combine sample totals so slower or larger responses are weighted by time.
def print_summary(label: str, samples: list[Sample]) -> None:
    total_latency = sum(sample.latency_seconds for sample in samples)
    total_bytes = sum(sample.response_bytes for sample in samples)
    average_latency_ms = total_latency / len(samples) * 1000
    average_speed = total_bytes / total_latency
    print(
        f"SUMMARY {label}: samples={len(samples)} "
        f"avg_latency_ms={average_latency_ms:.2f} "
        f"avg_response_speed={average_speed:.2f} B/s "
        f"total_response_bytes={total_bytes}"
    )


# Run fixed samples, log failures, and summarize only successful responses.
def main() -> None:
    if not db.host:
        raise RuntimeError("Set DBSERV_URL in api/.env before running endpoint tests.")

    # Keep connection reuse scoped to this benchmark run.
    all_samples: list[Sample] = []
    failures: list[str] = []
    session = requests.Session()
    print(
        f"API endpoint benchmark: base_url={db.host} "
        f"samples_per_endpoint={SAMPLES_PER_ENDPOINT}"
    )
    print("Endpoints:")
    for endpoint in ENDPOINTS:
        print(f"  {endpoint}")

    # Measure each endpoint independently to make per-service latency visible.
    for endpoint in ENDPOINTS:
        endpoint_samples: list[Sample] = []
        url = f"{db.host}{endpoint}"
        for sample_number in range(1, SAMPLES_PER_ENDPOINT + 1):
            try:
                sample = sample_endpoint(session, db.host, endpoint)
                endpoint_samples.append(sample)
                all_samples.append(sample)
                print(
                    f"LOG endpoint={endpoint} sample={sample_number}/"
                    f"{SAMPLES_PER_ENDPOINT} status={sample.status_code} "
                    f"latency_ms={sample.latency_seconds * 1000:.2f} "
                    f"response_bytes={sample.response_bytes} "
                    f"response_speed={sample.bytes_per_second:.2f} B/s"
                )
            except requests.RequestException as error:
                failures.append(f"{endpoint} sample {sample_number}: {error}")
                print(
                    f"ERROR endpoint={endpoint} sample={sample_number}/"
                    f"{SAMPLES_PER_ENDPOINT} url={url} error={error}"
                )

        if endpoint_samples:
            print_summary(endpoint, endpoint_samples)
        else:
            print(f"SUMMARY {endpoint}: no successful samples")

    session.close()

    # Report one roll-up across every successful endpoint sample.
    if all_samples:
        print_summary("ALL_ENDPOINTS", all_samples)

    # A partial run remains visible in logs but exits as a failed test.
    if failures:
        details = "\n".join(f"- {failure}" for failure in failures)
        raise RuntimeError(f"{len(failures)} endpoint sample(s) failed:\n{details}")


if __name__ == "__main__":
    main()
