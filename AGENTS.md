# AGENTS.md: ZeroAlloc.Collections

A NuGet library of pooled and stack-only collections (`PooledList<T>`, `RingBuffer<T>`,
`SpanDictionary<TKey,TValue>`, `PooledStack<T>`, `PooledQueue<T>`, `FixedSizeList<T>`) in two forms each:
a `ref struct` for synchronous hot paths and a `Heap*` class for fields and async code, plus
`ConcurrentHeapSpanDictionary`. One package ships: `ZeroAlloc.Collections`, which bundles the source
generators as an analyzer. Targets `netstandard2.1`, `net8.0`, `net9.0` and `net11.0`.

## Layout
- `src/ZeroAlloc.Collections`: the library. `src/ZeroAlloc.Collections.Generators` is a `netstandard2.0`
  Roslyn generator and analyzer, `IsPackable=false`; it ships only inside the library package.
- `tests/ZeroAlloc.Collections.Tests` is the xunit suite (targets `net9.0` and `net11.0`).
  `tests/ZeroAlloc.Collections.Benchmarks` is BenchmarkDotNet and is not part of any test run.
- `samples/ZeroAlloc.Collections.AotSmoke` is a NativeAOT app that CI publishes and runs. It targets
  `net11.0` on purpose, so the publish uses the newest ILCompiler.
- `docs/` is the documentation site source; `docs/plans` holds design notes.

## Build and test
SDK: `global.json` pins 11.0.100 with `rollForward: latestMinor`. Tests need the .NET 9 and .NET 11
runtimes; the 11.0 SDK supplies the latter.

CI job `build` (`.github/workflows/ci.yml`):
- `dotnet restore ZeroAlloc.Collections.slnx`
- `dotnet build ZeroAlloc.Collections.slnx --configuration Release --no-restore`
- `dotnet test ZeroAlloc.Collections.slnx --configuration Release --no-build --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults`
  (CI also adds a trx logger). Coverage must come from `--collect` plus the runsettings file, not the
  runsettings alone, or a module's hits can read 0.

CI job `aot-smoke`:
- `dotnet publish samples/ZeroAlloc.Collections.AotSmoke/ZeroAlloc.Collections.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out`
- `timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Collections.AotSmoke`
  Needs `clang` and `zlib1g-dev`.

CI job `baseline-checks` (`baseline-checks.yml`) runs the shared gitleaks, CodeQL and NuGetAudit
baseline. CI job `api-compat` runs the shared API compatibility check.

Fast inner loop (run locally, passes):
- `dotnet build ZeroAlloc.Collections.slnx -c Release`
- `dotnet test tests/ZeroAlloc.Collections.Tests -c Release --no-build -f net9.0 --filter "FullyQualifiedName~AllocationTests"`

Benchmarks run from `benchmarks.yml`, job `full`:
- `dotnet build tests/ZeroAlloc.Collections.Benchmarks -c Release`
- `dotnet run --project tests/ZeroAlloc.Collections.Benchmarks -c Release --no-build -- --filter "$FILTER"`

The benchmark project targets only `net9.0`, not the library's LTS `net8.0`, which the org rule below asks for.

## Conventions and gotchas
- Warnings are errors (`TreatWarningsAsErrors`, Meziantou.Analyzer, `EnforceCodeStyleInBuild`). Fix the
  cause; do not add to `NoWarn`. `MA0158` is already off because `System.Threading.Lock` is .NET 9+ and
  the library targets older frameworks. `MA0048` is off only under `tests/`.
- Public API is locked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`: RS0016 and RS0017 are errors.
  Any public addition or removal needs a line in `src/ZeroAlloc.Collections/PublicAPI.Unshipped.txt`.
  Do not edit `PublicAPI.Shipped.txt`; a release PR moves entries there.
- The same applies to generator diagnostics: new rules go in
  `src/ZeroAlloc.Collections.Generators/AnalyzerReleases.Unshipped.md`.
- The generator project is `netstandard2.0` with `EnforceExtendedAnalyzerRules`. Do not use APIs
  newer than netstandard2.0 there, and keep it incremental: `IncrementalCachingTests` guards caching.
  The tests project references it as an analyzer and as an assembly, so tests can call its `internal`
  helpers (`InternalsVisibleTo`).
- `AllocationTests` is the allocation contract. The `ref struct` collections must stay at 0 B per call
  (`AllocationGate.AssertBudget(0, ...)`); paths that allocate are pinned to their exact figure. A change
  that moves a number must change the test on purpose, not loosen the budget.
- `ref struct` collections cannot be class fields or be captured by lambdas. In `async` methods they may
  only be locals that do not live across an `await` (C# 13 and later). Tests that use them stay synchronous. Pooled types rent from `ArrayPool<T>.Shared` and
  must be disposed (diagnostic `ZAC001` flags a missing dispose).
- The library reports `IsAotCompatible` for `net8.0` and later. No reflection, no runtime code
  generation. New generator output must also publish cleanly in `AotSmoke`.
- Packing needs an explicit version: `dotnet pack -c Release -p:Version=X.Y.Z`. `Directory.Build.props`
  fails the pack with ZAVERSION001 or ZAVERSION002 otherwise, and a `--no-build` pack needs the same
  `-p:Version` on the earlier build. Releases are driven by release-please, not by hand.

<!-- org-core:start sha256=c42b83cb821d67a5547aafe02bbf9d3f36a7d19379a5d378bc38d452b7db8451 (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` |
| `Task.WhenAll` over tasks that already completed | check `IsCompletedSuccessfully`, await only what is pending |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)`, `decimal.GetBits(v)` | `Enum.IsDefined<E>`, `v.Scale` |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, exclude only that code with `WarningsNotAsErrors` and name the assembly.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
