# Resiliency State Machine & Circuit Breakers

To meet budget constraints and avoid the overhead of heavy third-party resilience libraries, Tsumari implements a **custom, zero-allocation, thread-safe Circuit Breaker** and **exponential backoff retry utility** specifically designed for low-memory (1GB RAM) environments.

---

## 🔄 Circuit Breaker States

The Circuit Breaker shields the bot and DeepL API by monitoring sequential failures and instantly failing fast when the translation engine is down:

```
            +--------- Success ---------+
            |                           |
            v                           |
      +-----------+   Failure >= 3    +----------+
  --->|  CLOSED   |------------------>|   OPEN   |
      +-----------+                   +----------+
            ^                              |
            |                         Cooldown (30s)
         Success                           |
            |                              v
      +-----------+                   +----------+
      | HALF-OPEN |<-- Test Request --| HALF-OPEN|
      +-----------+                   +----------+
            |                              |
            +----------- Fail -------------+
```

1.  **`Closed` (Normal Operation):** All translation requests are forwarded to the DeepL client. If a request fails, the failure counter increments. If it succeeds, the counter resets.
2.  **`Open` (Fault State):** If sequential failures reach the threshold of **3 consecutive errors**, the circuit trips to `Open` and starts a cooldown timer of **30 seconds**. 
    *   *Fast-Fail:* During this state, all incoming translation tasks instantly fail-fast and throw an `InvalidOperationException` without calling the DeepL API, conserving HTTP resources.
3.  **`Half-Open` (Test State):** After the 30-second cooldown expires, the next incoming request transitions the circuit to `Half-Open` and is allowed to run.
    *   *Recovery:* If this test request succeeds, the circuit returns to `Closed` (Service Restored).
    *   *Re-Trip:* If the test request fails, the circuit immediately returns to `Open` and starts another 30-second cooldown.

---

## 📈 Exponential Backoff with Jitter

To prevent the "thundering herd" problem and avoid hitting DeepL API rate limits during high-load scenarios, Tsumari's retry policy uses an exponential backoff algorithm backed by a randomized jitter factor.

### Backoff Calculation:
$$\text{Delay} = \text{Initial Delay} \times 2^{\text{attempt} - 1} \times \text{Jitter}$$

*   **Initial Delay:** 1 second.
*   **Jitter:** A random float factor between `0.5` and `1.5`.
*   **Maximum Cap:** Capped at 30 seconds to prevent excessively long waits.

This approach disperses request timings naturally, allowing temporary connection gaps or rate limit buckets to clear before retrying.
