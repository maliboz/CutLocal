# Failure and fallback state machine

```mermaid
stateDiagram-v2
    [*] --> Queued
    Queued --> PreparingModel
    PreparingModel --> Decoding: session ready
    PreparingModel --> Failed: missing/corrupt/incompatible model
    Decoding --> Preprocessing: valid bounded image
    Decoding --> Failed: unsupported/corrupt/too large
    Preprocessing --> Inferring
    Inferring --> Postprocessing: success
    Inferring --> CpuRetry: GPU device removed/OOM/provider failure and retry unused
    CpuRetry --> Postprocessing: CPU success
    CpuRetry --> Failed: CPU failure
    Inferring --> Failed: CPU failure or retry already used
    Postprocessing --> Encoding
    Encoding --> Completed: atomic move succeeds
    Encoding --> Failed: disk/permission/lock/encode failure
    Queued --> Cancelled: cancellation requested
    PreparingModel --> Cancelled: cancellation observed
    Decoding --> Cancelled: cancellation observed
    Inferring --> Cancelled: after current Run returns
    Postprocessing --> Cancelled: cancellation observed
    Encoding --> Cancelled: before final move
    Completed --> [*]
    Failed --> [*]
    Cancelled --> [*]
```

Every terminal result carries `ProcessingOutcome`, a localized-message key, stable log code, category, and retryability. UI receives no native stack trace. Logs receive the exception and sanitized context.

Expected file, model, provider, and cancellation failures are converted at their layer boundary. Truly unexpected exceptions are logged and rethrown to the global exception handlers; handlers do not silently swallow process corruption. Phase 2 implements both provider-initialization fallback and the explicit one-use GPU-to-CPU runtime edge. The failed GPU lease is invalidated and disposed before CPU acquisition.
