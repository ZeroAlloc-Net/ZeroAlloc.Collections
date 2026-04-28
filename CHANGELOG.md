# Changelog

## 1.0.0

Stability milestone — public API of `ZeroAlloc.Collections` is now considered stable. No code changes from 0.1.6; this release marks the transition out of pre-1.0 SemVer.

## [0.1.7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.6...v0.1.7) (2026-04-28)


### Documentation

* add GitHub Sponsors badge to README ([3f964f9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/3f964f9d8d99974f9749b8b7693f07726ea855bc))

## [0.1.6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.5...v0.1.6) (2026-04-24)


### Features

* **collections:** ConcurrentHeapSpanDictionary - thread-safe pooled hash map ([#13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/issues/13)) ([5e928fc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/5e928fcfbcb646cebb384343b8a7c7ce3c8f5cb8))

## [0.1.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.4...v0.1.5) (2026-04-01)


### Bug Fixes

* show package icon on NuGet.org ([14f3608](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/14f3608c11a79e247c6fbef17b7e93c02c3d7321))
* show package icon on NuGet.org ([e49920b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/e49920b43c289b0842f526ae73ccb8ce3cc41931))

## [0.1.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.3...v0.1.4) (2026-04-01)


### Bug Fixes

* set slug: / on getting-started for root URL ([d121494](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/d121494dd0056ef2297720ce914aeca55c49b17d))

## [0.1.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.2...v0.1.3) (2026-03-31)


### Bug Fixes

* repair release pack — NoBuild conflict, XML docs, and analyzer release tracking ([1bab2c9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/1bab2c9d6638f137c833f49139f9e67cd481ceac))
* repair release pack — NoBuild conflict, XML docs, and analyzer tracking ([686208a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/686208ab042a2ebd0fe45b936a84133b265a663a))

## [0.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.1...v0.1.2) (2026-03-31)


### Bug Fixes

* address all code review issues — pooling, correctness, and API consistency ([d596b81](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/d596b817efd3af429affa779538d4619e770bc0f))
* generator diagnostics, incremental caching, and RingBuffer over-clear ([df9e710](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/df9e7102dd1afc7d98eaa0661c44a2f3984b6e93))
* generator diagnostics, incremental caching, and RingBuffer over-clear ([f137fd5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/f137fd52b4a0afe05881963a45288898b2d9ef59))

## [0.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/compare/v0.1.0...v0.1.1) (2026-03-31)


### Features

* **fixed-size-list:** add FixedSizeList&lt;T&gt; ref struct ([e06bbb3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/e06bbb32f25adc2e5eeaebe7fe8059fe74510868))
* **fixed-size-list:** add HeapFixedSizeList&lt;T&gt; ([636041d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/636041db02fa438e165a4948d9d82fadefa6de09))
* **generators:** add source generators, marker attributes, and diagnostic analyzer ([7d6ce1a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/7d6ce1ae2b3008d4d6295c70ca5d052aed757ba6))
* **pooled-list:** add HeapPooledList&lt;T&gt; with IList&lt;T&gt; support ([46bd646](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/46bd64615b71a403f9afde66b2d43916681265da))
* **pooled-list:** add PooledList&lt;T&gt; ref struct with full API ([c4b32b1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/c4b32b142b297e7f1727c33432844e7b64d4198e))
* **pooled-queue:** add HeapPooledQueue&lt;T&gt; ([3b376c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/3b376c7a07a6dbf5289a28631bd5fd9691a02e67))
* **pooled-queue:** add PooledQueue&lt;T&gt; ref struct ([ee8a64d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/ee8a64d0c6354d17870d3c175f77119b5f8ceeb3))
* **pooled-stack:** add HeapPooledStack&lt;T&gt; ([06a1e84](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/06a1e844346e3eda09dce52a02ba6bbe2c1f211f))
* **pooled-stack:** add PooledStack&lt;T&gt; ref struct ([013df97](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/013df976d651833f440dc458afb1fcf6fc0bd0a6))
* **ring-buffer:** add HeapRingBuffer&lt;T&gt; with IReadOnlyCollection support ([d308932](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/d3089324f07f383e1a818cb59278b7f8b63650dc))
* **ring-buffer:** add RingBuffer&lt;T&gt; ref struct with TryWrite/TryRead and enumerator ([317400c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/317400cabedb6b8371d31351d8d04c4170cd6311))
* **span-dictionary:** add HeapSpanDictionary&lt;TKey,TValue&gt; ([9761091](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/97610912d14d51e76c2b8baca9dd0167588bfb5b))
* **span-dictionary:** add SpanDictionary&lt;TKey,TValue&gt; with open addressing and enumerator ([63cae9c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/63cae9c606bdda06b326b2c3e0a6cdf7f42a3690))


### Performance

* **benchmarks:** add benchmarks for all collection types ([9923368](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/992336853f1c9d342ff072f70886b3887f3238f4))


### Documentation

* add collection reference pages ([c34690c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/c34690ccf0ffb8b08ade9ec79a2d6031102d75e0))
* add cookbook recipes ([aff38c2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/aff38c2f5a8816d0f0ca3ade42b42c728129fa67))
* add fixed-size-list, source generators, diagnostics, performance, and testing guides ([10ac794](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/10ac794ea2224a6f529adc19fdb867bcecba28ec))
* add implementation plan (32 tasks, 11 phases) ([c589a48](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/c589a48bbefc1811188992d3621931b259253987))
* add README and assets placeholder ([856a706](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/856a7066151dd1f4effcdb930e2d7e71dedb031f))
* add README, docs index, and getting started guide ([9d11bfe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/9d11bfe45576452c74074d5e2107910ad75870ea))
* add ZeroAlloc.Collections design document ([1aff34d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/commit/1aff34d4d6f73892f2fe7e59bb0b182f0012b3cf))
