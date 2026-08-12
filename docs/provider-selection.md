# Provider selection matrix

## Decision matrix

| Provider | Windows support | Offline/self-contained behavior | Concurrency | Failure behavior | Current status |
|---|---|---|---|---|---|
| CPU | Windows 10/11 x64 | Included in the bundled DirectML ORT package; always available | One `Run` per session; reserve one detected physical core; later batch concurrency capped at 2 | Return typed inference error; no lower provider exists | Implemented |
| DirectML | DirectX 12 hardware; Windows 10 1903+ is the documented baseline | DirectML runtime is a package dependency; no runtime download | Exactly 1 `Run` per session | Invalidate/dispose failed GPU session, retry the item once on CPU | Implemented |
| Windows ML | Current catalog/provider support varies by OS/device | Catalog readiness paths can download providers, so CutLocal does not ship or call them in the offline engine | Not activated | A `WindowsMl` policy request currently falls through to DirectML, then CPU | Investigation complete; optional offline-safe integration deferred |
| Auto | Policy, not an execution provider | Never initiates a network request | Chosen provider limit | DirectML devices ordered by dedicated memory, then CPU; validation and warm-up at each step | Implemented |

The DirectML rules come from the [official execution-provider documentation](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html): sequential execution, disabled memory patterns, no concurrent `Run` calls on one session, and adapter index 0 not assumed to be the fastest GPU. DirectML is in sustained engineering while new Windows investment moves toward Windows ML.

The current Windows ML catalog can dynamically obtain providers and its readiness path may download them. CutLocal therefore does not call an ensure/install method during discovery or inference. Shipping Windows ML later requires proof that the selected provider is already ready or self-contained and that no network/machine mutation occurs.

## Implemented Auto state order

1. Enumerate DXGI adapters in DirectML device-id order, discard software/non-DirectX-12 devices, and retain stable LUID identity; do not mutate the machine or access the network.
2. Filter candidates against the model manifest and order DirectML devices by dedicated video memory, then DXGI index.
3. Validate model metadata and create a session for the preferred candidate.
4. Warm up using synthetic tensor content at the model's fixed input shape.
5. If GPU initialization or validation fails, dispose it and try the next device/provider.
6. During GPU inference, classify device removed, OOM, and provider failure; invalidate/dispose the session and retry that item once on CPU.
7. Never retry a CPU failure automatically and never retry the same GPU failure more than once.

GPU selection exposes stable adapter identity and the current enumeration index; Phase 3 settings persistence will store the identity and resolve the current index on startup. Hardware benchmark results contain OS, runtime, adapter, model, and model version; persistence and driver-version staleness checks belong to Phase 3/5 settings work.
