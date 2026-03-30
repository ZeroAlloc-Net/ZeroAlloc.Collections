# ZeroAlloc.Collections Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a high-performance, zero/minimal-allocation collections library for .NET with ref struct and heap variants, plus source generators.

**Architecture:** Each collection has a `ref struct` variant (zero heap alloc, stack-only) and a `Heap*` class variant (implements interfaces, usable in async/fields). Pooled collections use `ArrayPool<T>.Shared` by default with custom pool overloads. Source generators live in a separate netstandard2.0 project but ship in a single NuGet package.

**Tech Stack:** C# / .NET (netstandard2.1, net8.0, net9.0), xUnit, BenchmarkDotNet, Roslyn source generators, release-please, commitlint, renovate

---

## Phase 1: Project Scaffold

### Task 1: Initialize repo and config files

**Files:**
- Create: `.gitignore`
- Create: `.commitlintrc.yml`
- Create: `GitVersion.yml`
- Create: `renovate.json`
- Create: `LICENSE`
- Create: `.config/dotnet-tools.json`
- Create: `release-please-config.json`
- Create: `.release-please-manifest.json`

**Step 1: Create .gitignore**

```
## .NET / C#
bin/
obj/
*.user
*.suo
*.vs/
.vs/
*.nupkg
*.snupkg

## Rider / VS
.idea/
*.DotSettings.user

## OS
.DS_Store
Thumbs.db

## BenchmarkDotNet
BenchmarkDotNet.Artifacts/
```

**Step 2: Create .commitlintrc.yml**

```yaml
extends:
  - "@commitlint/config-conventional"

rules:
  type-enum:
    - 2
    - always
    - - feat
      - fix
      - docs
      - style
      - refactor
      - perf
      - test
      - build
      - ci
      - chore
      - revert

  scope-enum:
    - 1
    - always
    - - core
      - pooled-list
      - ring-buffer
      - span-dictionary
      - pooled-stack
      - pooled-queue
      - fixed-size-list
      - generators
      - benchmarks
      - ci
      - deps

  subject-case:
    - 2
    - never
    - - sentence-case
      - start-case
      - pascal-case
      - upper-case

  header-max-length:
    - 2
    - always
    - 100
```

**Step 3: Create GitVersion.yml**

```yaml
mode: ContinuousDeployment
tag-prefix: v
major-version-bump-message: "^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\\(.*\\))?!:"
minor-version-bump-message: "^feat(\\(.*\\))?:"
patch-version-bump-message: "^fix(\\(.*\\))?:"
branches:
  main:
    regex: ^main$
    label: alpha
  release:
    regex: ^release/.*$
    label: rc
```

**Step 4: Create renovate.json**

```json
{
  "$schema": "https://docs.renovatebot.com/renovate-schema.json",
  "extends": ["config:recommended"],
  "schedule": ["before 6am on monday"],
  "timezone": "Europe/Amsterdam",
  "labels": ["dependencies"],
  "packageRules": [
    {
      "description": "Ignore internal ZeroAlloc packages",
      "matchPackagePrefixes": ["ZeroAlloc."],
      "enabled": false
    },
    {
      "description": "Group Roslyn analyzer packages",
      "matchPackageNames": [
        "Meziantou.Analyzer",
        "Roslynator.Analyzers",
        "ErrorProne.NET.CoreAnalyzers",
        "ErrorProne.NET.Structs",
        "NetFabric.Hyperlinq.Analyzer"
      ],
      "groupName": "Roslyn analyzers"
    },
    {
      "description": "Group xunit packages",
      "matchPackagePrefixes": ["xunit"],
      "groupName": "xunit"
    },
    {
      "description": "Group Microsoft.CodeAnalysis packages",
      "matchPackagePrefixes": ["Microsoft.CodeAnalysis."],
      "groupName": "Microsoft.CodeAnalysis"
    },
    {
      "description": "Group GitHub Actions",
      "matchManagers": ["github-actions"],
      "groupName": "GitHub Actions"
    },
    {
      "description": "Automerge patch updates",
      "matchUpdateTypes": ["patch"],
      "automerge": true
    }
  ]
}
```

**Step 5: Create LICENSE (MIT), .config/dotnet-tools.json, release-please configs**

`.config/dotnet-tools.json`:
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "gitversion.tool": {
      "version": "6.6.2",
      "commands": ["dotnet-gitversion"],
      "rollForward": false
    }
  }
}
```

`release-please-config.json`:
```json
{
  "packages": {
    ".": {
      "release-type": "simple",
      "bump-minor-pre-major": true,
      "bump-patch-for-minor-pre-major": true,
      "changelog-sections": [
        { "type": "feat", "section": "Features" },
        { "type": "fix", "section": "Bug Fixes" },
        { "type": "perf", "section": "Performance" },
        { "type": "refactor", "section": "Refactoring" },
        { "type": "docs", "section": "Documentation" },
        { "type": "test", "section": "Tests" },
        { "type": "build", "section": "Build", "hidden": true },
        { "type": "ci", "section": "CI", "hidden": true },
        { "type": "chore", "section": "Miscellaneous", "hidden": true }
      ]
    }
  },
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json"
}
```

`.release-please-manifest.json`:
```json
{
  ".": "0.1.0"
}
```

`LICENSE`: MIT license, Copyright (c) Marcel Roozekrans

**Step 6: Commit**

```bash
git add -A
git commit -m "chore: initialize repo config files"
```

---

### Task 2: Create Directory.Build.props and solution file

**Files:**
- Create: `Directory.Build.props`
- Create: `ZeroAlloc.Collections.slnx`

**Step 1: Create Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>

    <!-- NuGet package metadata -->
    <Authors>Marcel Roozekrans</Authors>
    <Company>Marcel Roozekrans</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://collections.zeroalloc.net</PackageProjectUrl>
    <RepositoryUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <Description>Zero-allocation, high-performance collection types for .NET with ref struct and heap variants.</Description>
    <PackageTags>collections;zero-allocation;pooled;ring-buffer;span;high-performance</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>
    <Copyright>Copyright (c) Marcel Roozekrans</Copyright>
    <VersionPrefix>0.1.0</VersionPrefix>
  </PropertyGroup>
  <ItemGroup Condition="'$(IsPackable)' != 'false'">
    <None Include="$(MSBuildThisFileDirectory)README.md" Pack="true" PackagePath="\" />
    <None Include="$(MSBuildThisFileDirectory)assets\icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' != 'netstandard2.0'">
    <PackageReference Include="Meziantou.Analyzer" Version="3.0.24">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ErrorProne.NET.Structs" Version="0.1.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Step 2: Create ZeroAlloc.Collections.slnx**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj" />
    <Project Path="src/ZeroAlloc.Collections.Generators/ZeroAlloc.Collections.Generators.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ZeroAlloc.Collections.Tests/ZeroAlloc.Collections.Tests.csproj" />
    <Project Path="tests/ZeroAlloc.Collections.Benchmarks/ZeroAlloc.Collections.Benchmarks.csproj" />
  </Folder>
</Solution>
```

**Step 3: Commit**

```bash
git add Directory.Build.props ZeroAlloc.Collections.slnx
git commit -m "build: add Directory.Build.props and solution file"
```

---

### Task 3: Create source projects

**Files:**
- Create: `src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj`
- Create: `src/ZeroAlloc.Collections.Generators/ZeroAlloc.Collections.Generators.csproj`

**Step 1: Create the core library csproj**

`src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0;net9.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- Bundle the source generator into the NuGet package -->
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Collections.Generators/ZeroAlloc.Collections.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

  <!-- Pack generator DLL into analyzers folder -->
  <ItemGroup>
    <None Include="../ZeroAlloc.Collections.Generators/bin/$(Configuration)/netstandard2.0/ZeroAlloc.Collections.Generators.dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>
</Project>
```

**Step 2: Create the generators csproj**

`src/ZeroAlloc.Collections.Generators/ZeroAlloc.Collections.Generators.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
  </ItemGroup>
</Project>
```

**Step 3: Add placeholder files so the projects compile**

`src/ZeroAlloc.Collections/Placeholder.cs`:
```csharp
// Placeholder — will be replaced by actual collection types.
```

`src/ZeroAlloc.Collections.Generators/Placeholder.cs`:
```csharp
// Placeholder — will be replaced by actual generators.
```

**Step 4: Verify build**

Run: `dotnet build ZeroAlloc.Collections.slnx`
Expected: Build succeeded (warnings OK, no errors)

**Step 5: Commit**

```bash
git add src/
git commit -m "build: add core library and generators projects"
```

---

### Task 4: Create test and benchmark projects

**Files:**
- Create: `tests/ZeroAlloc.Collections.Tests/ZeroAlloc.Collections.Tests.csproj`
- Create: `tests/ZeroAlloc.Collections.Benchmarks/ZeroAlloc.Collections.Benchmarks.csproj`

**Step 1: Create test project csproj**

`tests/ZeroAlloc.Collections.Tests/ZeroAlloc.Collections.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create benchmark project csproj**

`tests/ZeroAlloc.Collections.Benchmarks/ZeroAlloc.Collections.Benchmarks.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj" />
  </ItemGroup>
</Project>
```

**Step 3: Add placeholder test**

`tests/ZeroAlloc.Collections.Tests/SmokeTest.cs`:
```csharp
namespace ZeroAlloc.Collections.Tests;

public class SmokeTest
{
    [Fact]
    public void Placeholder_ShouldPass() => Assert.True(true);
}
```

**Step 4: Add benchmark placeholder**

`tests/ZeroAlloc.Collections.Benchmarks/Program.cs`:
```csharp
using BenchmarkDotNet.Running;

// Uncomment when benchmarks are added:
// BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
Console.WriteLine("No benchmarks configured yet.");
```

**Step 5: Verify build and test**

Run: `dotnet build ZeroAlloc.Collections.slnx`
Expected: Build succeeded

Run: `dotnet test ZeroAlloc.Collections.slnx --verbosity normal`
Expected: 1 test passed

**Step 6: Commit**

```bash
git add tests/
git commit -m "build: add test and benchmark projects"
```

---

### Task 5: Add GitHub workflows

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`

**Step 1: Create CI workflow**

`.github/workflows/ci.yml`:
```yaml
name: CI

on:
  push:
    branches: [main, 'release-please--**']
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  lint-commits:
    runs-on: ubuntu-latest
    if: github.event_name == 'pull_request'

    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Lint commit messages
        uses: wagoid/commitlint-github-action@v6
        with:
          configFile: .commitlintrc.yml

  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            9.0.x

      - name: Restore tools
        run: dotnet tool restore

      - name: Calculate version
        id: gitversion
        run: |
          version=$(dotnet dotnet-gitversion /output json /showvariable SemVer)
          echo "semver=$version" >> $GITHUB_OUTPUT
          echo "Calculated version: $version"

      - name: Restore
        run: dotnet restore ZeroAlloc.Collections.slnx

      - name: Build
        run: dotnet build ZeroAlloc.Collections.slnx --configuration Release --no-restore

      - name: Test
        run: dotnet test ZeroAlloc.Collections.slnx --configuration Release --no-build --verbosity normal --logger "trx;LogFileName=test-results.trx"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/*.trx"
```

**Step 2: Create release workflow**

`.github/workflows/release.yml`:
```yaml
name: Release

on:
  push:
    branches: [main]

permissions:
  contents: write
  pull-requests: write
  packages: write

jobs:
  release-please:
    runs-on: ubuntu-latest

    outputs:
      release_created: ${{ steps.release.outputs.release_created }}
      version: ${{ steps.release.outputs.major }}.${{ steps.release.outputs.minor }}.${{ steps.release.outputs.patch }}
      tag_name: ${{ steps.release.outputs.tag_name }}

    steps:
      - name: Run release-please
        id: release
        uses: googleapis/release-please-action@v4
        with:
          config-file: release-please-config.json
          manifest-file: .release-please-manifest.json
          token: ${{ secrets.GITHUB_TOKEN }}

  publish:
    runs-on: ubuntu-latest
    needs: release-please
    if: needs.release-please.outputs.release_created == 'true'

    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            9.0.x

      - name: Restore tools
        run: dotnet tool restore

      - name: Restore
        run: dotnet restore ZeroAlloc.Collections.slnx

      - name: Build
        run: dotnet build ZeroAlloc.Collections.slnx --configuration Release --no-restore -p:Version=${{ needs.release-please.outputs.version }}

      - name: Test
        run: dotnet test ZeroAlloc.Collections.slnx --configuration Release --no-build

      - name: Pack
        run: dotnet pack src/ZeroAlloc.Collections/ZeroAlloc.Collections.csproj --configuration Release --no-build -p:Version=${{ needs.release-please.outputs.version }} -o ./artifacts

      - name: Push to NuGet
        run: dotnet nuget push "./artifacts/*.nupkg" --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
        env:
          NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}

      - name: Upload packages to GitHub Release
        run: gh release upload ${{ needs.release-please.outputs.tag_name }} ./artifacts/*.nupkg
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Step 3: Commit**

```bash
git add .github/
git commit -m "ci: add CI and release workflows"
```

---

### Task 6: Add README and assets placeholder

**Files:**
- Create: `README.md`
- Create: `assets/icon.png` (placeholder)

**Step 1: Create README.md**

```markdown
# ZeroAlloc.Collections

Zero-allocation, high-performance collection types for .NET.

## Collections

| Type | Ref Struct | Heap Variant | Description |
|------|-----------|--------------|-------------|
| `PooledList<T>` | Yes | `HeapPooledList<T>` | Pooled-backed growable list |
| `RingBuffer<T>` | Yes | `HeapRingBuffer<T>` | Fixed-capacity circular buffer |
| `SpanDictionary<TKey,TValue>` | Yes | `HeapSpanDictionary<TKey,TValue>` | Open-addressing hash map |
| `PooledStack<T>` | Yes | `HeapPooledStack<T>` | Pooled-backed LIFO stack |
| `PooledQueue<T>` | Yes | `HeapPooledQueue<T>` | Pooled-backed FIFO queue |
| `FixedSizeList<T>` | Yes | `HeapFixedSizeList<T>` | Stack-allocated fixed-capacity list |

## Installation

```bash
dotnet add package ZeroAlloc.Collections
```

## License

MIT
```

**Step 2: Create assets directory with placeholder icon**

Create `assets/` directory. Copy or generate a placeholder `icon.png` (1x1 pixel PNG is fine for now).

**Step 3: Commit**

```bash
git add README.md assets/
git commit -m "docs: add README and assets placeholder"
```

---

## Phase 2: PooledList\<T\> (ref struct)

### Task 7: Write failing tests for PooledList\<T\> construction and disposal

**Files:**
- Create: `tests/ZeroAlloc.Collections.Tests/PooledListTests.cs`
- Create: `src/ZeroAlloc.Collections/PooledList.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class PooledListTests
{
    [Fact]
    public void DefaultConstructor_CreatesEmptyList()
    {
        using var list = new PooledList<int>();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Constructor_WithCapacity_CreatesEmptyListWithCapacity()
    {
        using var list = new PooledList<int>(16);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var list = new PooledList<int>();
        list.Dispose();
        list.Dispose(); // should not throw
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: FAIL — `PooledList<int>` does not exist

**Step 3: Write minimal PooledList\<T\> implementation**

`src/ZeroAlloc.Collections/PooledList.cs`:
```csharp
using System.Buffers;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A zero-allocation, pooled-backed list. Returns buffers to the pool on disposal.
/// This is a ref struct — it cannot be stored on the heap or used in async methods.
/// Use <see cref="HeapPooledList{T}"/> for those scenarios.
/// </summary>
public ref struct PooledList<T>
{
    private T[]? _array;
    private int _count;
    private readonly ArrayPool<T> _pool;

    public PooledList() : this(0, ArrayPool<T>.Shared) { }

    public PooledList(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    public PooledList(int capacity, ArrayPool<T> pool)
    {
        _pool = pool;
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    public readonly int Count => _count;

    public void Dispose()
    {
        if (_array is not null)
        {
            _pool.Return(_array);
            _array = null;
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: 3 tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledList.cs tests/ZeroAlloc.Collections.Tests/PooledListTests.cs
git commit -m "feat(pooled-list): add PooledList<T> construction and disposal"
```

---

### Task 8: Add/indexer/count for PooledList\<T\>

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/PooledListTests.cs`
- Modify: `src/ZeroAlloc.Collections/PooledList.cs`

**Step 1: Write the failing tests**

Add to `PooledListTests.cs`:
```csharp
[Fact]
public void Add_SingleItem_IncrementsCount()
{
    using var list = new PooledList<int>();
    list.Add(42);
    Assert.Equal(1, list.Count);
}

[Fact]
public void Add_MultipleItems_AllAccessibleByIndex()
{
    using var list = new PooledList<int>();
    list.Add(1);
    list.Add(2);
    list.Add(3);
    Assert.Equal(3, list.Count);
    Assert.Equal(1, list[0]);
    Assert.Equal(2, list[1]);
    Assert.Equal(3, list[2]);
}

[Fact]
public void Indexer_OutOfRange_Throws()
{
    using var list = new PooledList<int>();
    list.Add(1);
    Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
    Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
}

[Fact]
public void Add_BeyondInitialCapacity_GrowsAutomatically()
{
    using var list = new PooledList<int>(2);
    for (int i = 0; i < 100; i++)
        list.Add(i);
    Assert.Equal(100, list.Count);
    for (int i = 0; i < 100; i++)
        Assert.Equal(i, list[i]);
}

[Fact]
public void Indexer_Set_UpdatesValue()
{
    using var list = new PooledList<int>();
    list.Add(1);
    list[0] = 99;
    Assert.Equal(99, list[0]);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: FAIL — `Add` and indexer do not exist

**Step 3: Implement Add, indexer, and grow logic**

Add to `PooledList<T>`:
```csharp
public readonly ref T this[int index]
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return ref _array![index];
    }
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void Add(T item)
{
    if (_array is null || _count == _array.Length)
        Grow();
    _array![_count++] = item;
}

private void Grow()
{
    int newCapacity = _array is null ? 4 : _array.Length * 2;
    var newArray = _pool.Rent(newCapacity);
    if (_array is not null)
    {
        Array.Copy(_array, newArray, _count);
        _pool.Return(_array);
    }
    _array = newArray;
}
```

Note: The `ref T` indexer provides both get and set via ref return on the ref struct variant. For netstandard2.1 compatibility, `ref` returns work fine.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledList.cs tests/ZeroAlloc.Collections.Tests/PooledListTests.cs
git commit -m "feat(pooled-list): add Add, indexer, and auto-grow"
```

---

### Task 9: AsSpan, Clear, and ToArray for PooledList\<T\>

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/PooledListTests.cs`
- Modify: `src/ZeroAlloc.Collections/PooledList.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void AsSpan_ReturnsCorrectSlice()
{
    using var list = new PooledList<int>();
    list.Add(10);
    list.Add(20);
    var span = list.AsSpan();
    Assert.Equal(2, span.Length);
    Assert.Equal(10, span[0]);
    Assert.Equal(20, span[1]);
}

[Fact]
public void AsReadOnlySpan_ReturnsCorrectSlice()
{
    using var list = new PooledList<int>();
    list.Add(10);
    ReadOnlySpan<int> span = list.AsReadOnlySpan();
    Assert.Equal(1, span.Length);
}

[Fact]
public void Clear_ResetsCountButKeepsBuffer()
{
    using var list = new PooledList<int>(8);
    list.Add(1);
    list.Add(2);
    list.Clear();
    Assert.Equal(0, list.Count);
}

[Fact]
public void ToArray_CopiesElements()
{
    using var list = new PooledList<int>();
    list.Add(5);
    list.Add(10);
    var arr = list.ToArray();
    Assert.Equal(new[] { 5, 10 }, arr);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: FAIL

**Step 3: Implement AsSpan, AsReadOnlySpan, Clear, ToArray**

Add to `PooledList<T>`:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly Span<T> AsSpan() => _array is null ? Span<T>.Empty : _array.AsSpan(0, _count);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly ReadOnlySpan<T> AsReadOnlySpan() => AsSpan();

public void Clear()
{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
        Array.Clear(_array, 0, _count);
    _count = 0;
}

public readonly T[] ToArray()
{
    if (_count == 0) return Array.Empty<T>();
    var result = new T[_count];
    Array.Copy(_array!, result, _count);
    return result;
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledList.cs tests/ZeroAlloc.Collections.Tests/PooledListTests.cs
git commit -m "feat(pooled-list): add AsSpan, Clear, and ToArray"
```

---

### Task 10: Zero-alloc enumerator for PooledList\<T\>

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/PooledListTests.cs`
- Modify: `src/ZeroAlloc.Collections/PooledList.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void Foreach_EnumeratesAllItems()
{
    using var list = new PooledList<int>();
    list.Add(1);
    list.Add(2);
    list.Add(3);

    var results = new List<int>();
    foreach (ref readonly var item in list)
        results.Add(item);

    Assert.Equal(new[] { 1, 2, 3 }, results);
}

[Fact]
public void Enumerator_EmptyList_NoIterations()
{
    using var list = new PooledList<int>();
    int count = 0;
    foreach (ref readonly var item in list)
        count++;
    Assert.Equal(0, count);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: FAIL

**Step 3: Implement GetEnumerator with ref struct Enumerator**

Add to `PooledList<T>`:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly Enumerator GetEnumerator() => new(AsSpan());

public ref struct Enumerator
{
    private readonly Span<T> _span;
    private int _index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Enumerator(Span<T> span)
    {
        _span = span;
        _index = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() => ++_index < _span.Length;

    public readonly ref readonly T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _span[_index];
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledList.cs tests/ZeroAlloc.Collections.Tests/PooledListTests.cs
git commit -m "feat(pooled-list): add zero-alloc ref struct enumerator"
```

---

### Task 11: Remove, Insert, Contains for PooledList\<T\>

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/PooledListTests.cs`
- Modify: `src/ZeroAlloc.Collections/PooledList.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void RemoveAt_RemovesAndShifts()
{
    using var list = new PooledList<int>();
    list.Add(1);
    list.Add(2);
    list.Add(3);
    list.RemoveAt(1);
    Assert.Equal(2, list.Count);
    Assert.Equal(1, list[0]);
    Assert.Equal(3, list[1]);
}

[Fact]
public void Insert_ShiftsElementsRight()
{
    using var list = new PooledList<int>();
    list.Add(1);
    list.Add(3);
    list.Insert(1, 2);
    Assert.Equal(3, list.Count);
    Assert.Equal(1, list[0]);
    Assert.Equal(2, list[1]);
    Assert.Equal(3, list[2]);
}

[Fact]
public void Contains_ReturnsTrueForExistingItem()
{
    using var list = new PooledList<int>();
    list.Add(42);
    Assert.True(list.Contains(42));
    Assert.False(list.Contains(99));
}

[Fact]
public void IndexOf_ReturnsCorrectIndex()
{
    using var list = new PooledList<int>();
    list.Add(10);
    list.Add(20);
    Assert.Equal(0, list.IndexOf(10));
    Assert.Equal(1, list.IndexOf(20));
    Assert.Equal(-1, list.IndexOf(99));
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: FAIL

**Step 3: Implement RemoveAt, Insert, Contains, IndexOf**

Add to `PooledList<T>`:
```csharp
public void RemoveAt(int index)
{
    if ((uint)index >= (uint)_count)
        throw new ArgumentOutOfRangeException(nameof(index));
    _count--;
    if (index < _count)
        Array.Copy(_array!, index + 1, _array!, index, _count - index);
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        _array![_count] = default!;
}

public void Insert(int index, T item)
{
    if ((uint)index > (uint)_count)
        throw new ArgumentOutOfRangeException(nameof(index));
    if (_array is null || _count == _array.Length)
        Grow();
    if (index < _count)
        Array.Copy(_array!, index, _array!, index + 1, _count - index);
    _array![index] = item;
    _count++;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly bool Contains(T item) => IndexOf(item) >= 0;

public readonly int IndexOf(T item)
{
    if (_array is null) return -1;
    return Array.IndexOf(_array, item, 0, _count);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledList.cs tests/ZeroAlloc.Collections.Tests/PooledListTests.cs
git commit -m "feat(pooled-list): add RemoveAt, Insert, Contains, IndexOf"
```

---

## Phase 3: HeapPooledList\<T\> (class variant)

### Task 12: HeapPooledList\<T\> wrapping PooledList\<T\> logic

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapPooledList.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapPooledListTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class HeapPooledListTests
{
    [Fact]
    public void Implements_IList()
    {
        using var list = new HeapPooledList<int>();
        IList<int> ilist = list;
        ilist.Add(1);
        Assert.Equal(1, ilist.Count);
        Assert.Equal(1, ilist[0]);
    }

    [Fact]
    public void Implements_IDisposable()
    {
        IDisposable list = new HeapPooledList<int>();
        list.Dispose();
    }

    [Fact]
    public void Add_And_Enumerate()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.Equal(new[] { 1, 2, 3 }, list.ToArray());
    }

    [Fact]
    public void CanBeStoredAsField()
    {
        var holder = new ListHolder<int>();
        holder.List.Add(42);
        Assert.Equal(42, holder.List[0]);
        holder.Dispose();
    }

    private class ListHolder<T> : IDisposable
    {
        public HeapPooledList<T> List { get; } = new();
        public void Dispose() => List.Dispose();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "HeapPooledListTests"`
Expected: FAIL

**Step 3: Implement HeapPooledList\<T\>**

`src/ZeroAlloc.Collections/HeapPooledList.cs`:
```csharp
using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A pooled-backed list that can be stored on the heap, used in async methods, and implements standard interfaces.
/// Use <see cref="PooledList{T}"/> for the zero-allocation ref struct variant.
/// </summary>
public sealed class HeapPooledList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    private T[]? _array;
    private int _count;
    private readonly ArrayPool<T> _pool;

    public HeapPooledList() : this(0, ArrayPool<T>.Shared) { }
    public HeapPooledList(int capacity) : this(capacity, ArrayPool<T>.Shared) { }
    public HeapPooledList(int capacity, ArrayPool<T> pool)
    {
        _pool = pool;
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _array![index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _array![index] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_array is null || _count == _array.Length) Grow();
        _array![_count++] = item;
    }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_array is null || _count == _array.Length) Grow();
        if (index < _count) Array.Copy(_array!, index, _array!, index + 1, _count - index);
        _array![index] = item;
        _count++;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        _count--;
        if (index < _count) Array.Copy(_array!, index + 1, _array!, index, _count - index);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _array![_count] = default!;
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;
    public int IndexOf(T item) => _array is null ? -1 : Array.IndexOf(_array, item, 0, _count);

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
            Array.Clear(_array, 0, _count);
        _count = 0;
    }

    public void CopyTo(T[] array, int arrayIndex) =>
        Array.Copy(_array!, 0, array, arrayIndex, _count);

    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        Array.Copy(_array!, result, _count);
        return result;
    }

    public Span<T> AsSpan() => _array is null ? Span<T>.Empty : _array.AsSpan(0, _count);
    public ReadOnlySpan<T> AsReadOnlySpan() => AsSpan();

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _array![i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Grow()
    {
        int newCapacity = _array is null ? 4 : _array.Length * 2;
        var newArray = _pool.Rent(newCapacity);
        if (_array is not null)
        {
            Array.Copy(_array, newArray, _count);
            _pool.Return(_array);
        }
        _array = newArray;
    }

    public void Dispose()
    {
        if (_array is not null)
        {
            _pool.Return(_array);
            _array = null;
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "HeapPooledListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/HeapPooledList.cs tests/ZeroAlloc.Collections.Tests/HeapPooledListTests.cs
git commit -m "feat(pooled-list): add HeapPooledList<T> with IList<T> support"
```

---

## Phase 4: RingBuffer\<T\>

### Task 13: RingBuffer\<T\> ref struct — construction, TryWrite, TryRead

**Files:**
- Create: `src/ZeroAlloc.Collections/RingBuffer.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/RingBufferTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class RingBufferTests
{
    [Fact]
    public void NewBuffer_IsEmpty()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.True(buf.IsEmpty);
        Assert.False(buf.IsFull);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void TryWrite_TryRead_SingleItem()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.True(buf.TryWrite(42));
        Assert.True(buf.TryRead(out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryWrite_WhenFull_ReturnsFalse()
    {
        using var buf = new RingBuffer<int>(2);
        Assert.True(buf.TryWrite(1));
        Assert.True(buf.TryWrite(2));
        Assert.True(buf.IsFull);
        Assert.False(buf.TryWrite(3));
    }

    [Fact]
    public void TryRead_WhenEmpty_ReturnsFalse()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.False(buf.TryRead(out _));
    }

    [Fact]
    public void Wraps_Around_Correctly()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryRead(out _); // removes 1
        buf.TryRead(out _); // removes 2
        buf.TryWrite(3);
        buf.TryWrite(4);
        buf.TryWrite(5);
        buf.TryWrite(6);
        Assert.True(buf.TryRead(out var v1));
        Assert.Equal(3, v1);
        Assert.True(buf.TryRead(out var v2));
        Assert.Equal(4, v2);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "RingBufferTests"`
Expected: FAIL

**Step 3: Implement RingBuffer\<T\>**

`src/ZeroAlloc.Collections/RingBuffer.cs`:
```csharp
using System.Buffers;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A fixed-capacity circular buffer. Zero heap allocation (ref struct).
/// Use <see cref="HeapRingBuffer{T}"/> for heap-storable variant.
/// </summary>
public ref struct RingBuffer<T>
{
    private T[]? _array;
    private readonly int _capacity;
    private int _head; // read position
    private int _tail; // write position
    private int _count;
    private readonly ArrayPool<T> _pool;

    public RingBuffer(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    public RingBuffer(int capacity, ArrayPool<T> pool)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _pool = pool;
        _array = pool.Rent(capacity);
        _capacity = capacity;
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    public readonly int Count => _count;
    public readonly bool IsEmpty => _count == 0;
    public readonly bool IsFull => _count == _capacity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(T item)
    {
        if (_count == _capacity) return false;
        _array![_tail] = item;
        _tail = (_tail + 1) % _capacity;
        _count++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(out T item)
    {
        if (_count == 0)
        {
            item = default!;
            return false;
        }
        item = _array![_head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _array[_head] = default!;
        _head = (_head + 1) % _capacity;
        _count--;
        return true;
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
            Array.Clear(_array, 0, _array.Length);
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    public void Dispose()
    {
        if (_array is not null)
        {
            _pool.Return(_array);
            _array = null;
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "RingBufferTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/RingBuffer.cs tests/ZeroAlloc.Collections.Tests/RingBufferTests.cs
git commit -m "feat(ring-buffer): add RingBuffer<T> with TryWrite/TryRead"
```

---

### Task 14: RingBuffer\<T\> enumerator and TryPeek

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/RingBufferTests.cs`
- Modify: `src/ZeroAlloc.Collections/RingBuffer.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void TryPeek_ReturnsHeadWithoutRemoving()
{
    using var buf = new RingBuffer<int>(4);
    buf.TryWrite(42);
    Assert.True(buf.TryPeek(out var value));
    Assert.Equal(42, value);
    Assert.Equal(1, buf.Count); // not removed
}

[Fact]
public void Foreach_EnumeratesInFifoOrder()
{
    using var buf = new RingBuffer<int>(4);
    buf.TryWrite(1);
    buf.TryWrite(2);
    buf.TryWrite(3);

    var results = new List<int>();
    foreach (var item in buf)
        results.Add(item);

    Assert.Equal(new[] { 1, 2, 3 }, results);
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement TryPeek and Enumerator**

Add to `RingBuffer<T>`:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly bool TryPeek(out T item)
{
    if (_count == 0) { item = default!; return false; }
    item = _array![_head];
    return true;
}

public readonly Enumerator GetEnumerator() => new(_array!, _head, _count, _capacity);

public ref struct Enumerator
{
    private readonly T[] _array;
    private readonly int _head;
    private readonly int _count;
    private readonly int _capacity;
    private int _index;

    internal Enumerator(T[] array, int head, int count, int capacity)
    {
        _array = array;
        _head = head;
        _count = count;
        _capacity = capacity;
        _index = -1;
    }

    public bool MoveNext() => ++_index < _count;
    public readonly T Current => _array[(_head + _index) % _capacity];
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "RingBufferTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/RingBuffer.cs tests/ZeroAlloc.Collections.Tests/RingBufferTests.cs
git commit -m "feat(ring-buffer): add TryPeek and zero-alloc enumerator"
```

---

### Task 15: HeapRingBuffer\<T\>

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapRingBuffer.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapRingBufferTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class HeapRingBufferTests
{
    [Fact]
    public void Implements_IDisposable()
    {
        IDisposable buf = new HeapRingBuffer<int>(4);
        buf.Dispose();
    }

    [Fact]
    public void TryWrite_TryRead_RoundTrip()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        Assert.True(buf.TryRead(out var v));
        Assert.Equal(1, v);
    }

    [Fact]
    public void Enumerable_Works()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(10);
        buf.TryWrite(20);
        Assert.Equal(new[] { 10, 20 }, buf.ToArray());
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement HeapRingBuffer\<T\>**

`src/ZeroAlloc.Collections/HeapRingBuffer.cs`: class variant implementing `IReadOnlyCollection<T>`, `IDisposable` with the same circular buffer logic. Include `TryWrite`, `TryRead`, `TryPeek`, `Count`, `IsEmpty`, `IsFull`, `Clear`, `ToArray`, `GetEnumerator`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "HeapRingBufferTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/HeapRingBuffer.cs tests/ZeroAlloc.Collections.Tests/HeapRingBufferTests.cs
git commit -m "feat(ring-buffer): add HeapRingBuffer<T> with IReadOnlyCollection support"
```

---

## Phase 5: PooledStack\<T\>

### Task 16: PooledStack\<T\> ref struct

**Files:**
- Create: `src/ZeroAlloc.Collections/PooledStack.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/PooledStackTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class PooledStackTests
{
    [Fact]
    public void Push_Pop_Lifo()
    {
        using var stack = new PooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        Assert.True(stack.TryPop(out var v));
        Assert.Equal(3, v);
    }

    [Fact]
    public void TryPeek_ReturnsTopWithoutRemoving()
    {
        using var stack = new PooledStack<int>();
        stack.Push(42);
        Assert.True(stack.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void TryPop_WhenEmpty_ReturnsFalse()
    {
        using var stack = new PooledStack<int>();
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void Grows_Automatically()
    {
        using var stack = new PooledStack<int>(2);
        for (int i = 0; i < 50; i++) stack.Push(i);
        Assert.Equal(50, stack.Count);
        for (int i = 49; i >= 0; i--)
        {
            Assert.True(stack.TryPop(out var v));
            Assert.Equal(i, v);
        }
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement PooledStack\<T\>**

`src/ZeroAlloc.Collections/PooledStack.cs`: ref struct backed by `ArrayPool<T>`. Uses `_count` as the stack pointer. `Push` = `Add` to end, `TryPop` = remove from end (no shifting needed). Include `Dispose`, `Clear`, `AsSpan`, `GetEnumerator` (iterates top-to-bottom).

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledStackTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledStack.cs tests/ZeroAlloc.Collections.Tests/PooledStackTests.cs
git commit -m "feat(pooled-stack): add PooledStack<T> ref struct"
```

---

### Task 17: HeapPooledStack\<T\>

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapPooledStack.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapPooledStackTests.cs`

Follow same pattern as Task 15 — class variant implementing `IReadOnlyCollection<T>`, `IDisposable`. Tests cover `Push`, `TryPop`, `TryPeek`, enumeration, `IDisposable`.

**Commit:** `feat(pooled-stack): add HeapPooledStack<T>`

---

## Phase 6: PooledQueue\<T\>

### Task 18: PooledQueue\<T\> ref struct

**Files:**
- Create: `src/ZeroAlloc.Collections/PooledQueue.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/PooledQueueTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class PooledQueueTests
{
    [Fact]
    public void Enqueue_Dequeue_Fifo()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        Assert.True(queue.TryDequeue(out var v));
        Assert.Equal(1, v);
    }

    [Fact]
    public void TryPeek_ReturnsHeadWithoutRemoving()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(42);
        Assert.True(queue.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryDequeue_WhenEmpty_ReturnsFalse()
    {
        using var queue = new PooledQueue<int>();
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void Wraps_And_Grows()
    {
        using var queue = new PooledQueue<int>(4);
        for (int i = 0; i < 3; i++) queue.Enqueue(i);
        for (int i = 0; i < 3; i++) queue.TryDequeue(out _);
        // head is now at index 3, fill past capacity to trigger grow
        for (int i = 0; i < 10; i++) queue.Enqueue(i);
        Assert.Equal(10, queue.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(queue.TryDequeue(out var v));
            Assert.Equal(i, v);
        }
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement PooledQueue\<T\>**

`src/ZeroAlloc.Collections/PooledQueue.cs`: ref struct with circular array (like `RingBuffer` but growable). Uses `_head`, `_tail`, `_count`. `Enqueue` adds at tail, grows when full (copies in order). `TryDequeue` reads from head. Include `Dispose`, `Clear`, `GetEnumerator`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "PooledQueueTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/PooledQueue.cs tests/ZeroAlloc.Collections.Tests/PooledQueueTests.cs
git commit -m "feat(pooled-queue): add PooledQueue<T> ref struct"
```

---

### Task 19: HeapPooledQueue\<T\>

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapPooledQueue.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapPooledQueueTests.cs`

Class variant implementing `IReadOnlyCollection<T>`, `IDisposable`. Same circular array logic. Tests cover `Enqueue`, `TryDequeue`, `TryPeek`, enumeration, disposal.

**Commit:** `feat(pooled-queue): add HeapPooledQueue<T>`

---

## Phase 7: SpanDictionary\<TKey, TValue\>

### Task 20: SpanDictionary\<TKey, TValue\> ref struct — core operations

**Files:**
- Create: `src/ZeroAlloc.Collections/SpanDictionary.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/SpanDictionaryTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class SpanDictionaryTests
{
    [Fact]
    public void Add_And_TryGetValue()
    {
        using var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");
        Assert.True(dict.TryGetValue(1, out var v));
        Assert.Equal("one", v);
    }

    [Fact]
    public void Indexer_Set_And_Get()
    {
        using var dict = new SpanDictionary<int, string>(4);
        dict[1] = "one";
        Assert.Equal("one", dict[1]);
    }

    [Fact]
    public void ContainsKey()
    {
        using var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.ContainsKey(1));
        Assert.False(dict.ContainsKey(2));
    }

    [Fact]
    public void Remove_ExistingKey()
    {
        using var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.Remove(1));
        Assert.False(dict.ContainsKey(1));
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void Grows_When_LoadFactor_Exceeded()
    {
        using var dict = new SpanDictionary<int, int>(4);
        for (int i = 0; i < 100; i++)
            dict.Add(i, i * 10);
        Assert.Equal(100, dict.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(dict.TryGetValue(i, out var v));
            Assert.Equal(i * 10, v);
        }
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement SpanDictionary\<TKey, TValue\>**

`src/ZeroAlloc.Collections/SpanDictionary.cs`: ref struct using open addressing with linear probing. Two pooled arrays: `Entry[] _entries` where `Entry` is a struct `{ TKey Key; TValue Value; int HashCode; EntryState State; }`. `EntryState` enum: `Empty, Occupied, Deleted`. Grow at 75% load factor. Backed by `ArrayPool<Entry>`.

Key methods: `Add`, `TryGetValue`, `ContainsKey`, `Remove`, `this[TKey]` (ref return on get for ref struct variant), `Count`, `Clear`, `Dispose`, `GetEnumerator`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "SpanDictionaryTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/SpanDictionary.cs tests/ZeroAlloc.Collections.Tests/SpanDictionaryTests.cs
git commit -m "feat(span-dictionary): add SpanDictionary<TKey,TValue> with open addressing"
```

---

### Task 21: SpanDictionary enumerator

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Tests/SpanDictionaryTests.cs`
- Modify: `src/ZeroAlloc.Collections/SpanDictionary.cs`

Test foreach over key-value pairs. Implement `GetEnumerator` returning ref struct enumerator that skips `Empty`/`Deleted` entries.

**Commit:** `feat(span-dictionary): add zero-alloc enumerator`

---

### Task 22: HeapSpanDictionary\<TKey, TValue\>

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapSpanDictionary.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapSpanDictionaryTests.cs`

Class variant implementing `IDictionary<TKey, TValue>`, `IReadOnlyDictionary<TKey, TValue>`, `IDisposable`. Same open-addressing logic.

**Commit:** `feat(span-dictionary): add HeapSpanDictionary<TKey,TValue>`

---

## Phase 8: FixedSizeList\<T\>

### Task 23: FixedSizeList\<T\> ref struct

**Files:**
- Create: `src/ZeroAlloc.Collections/FixedSizeList.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/FixedSizeListTests.cs`

**Step 1: Write the failing tests**

```csharp
namespace ZeroAlloc.Collections.Tests;

public class FixedSizeListTests
{
    [Fact]
    public void Add_WithinCapacity_Works()
    {
        var list = new FixedSizeList<int>(stackalloc int[4]);
        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
    }

    [Fact]
    public void Add_BeyondCapacity_Throws()
    {
        var list = new FixedSizeList<int>(stackalloc int[2]);
        list.Add(1);
        list.Add(2);
        Assert.Throws<InvalidOperationException>(() => list.Add(3));
    }

    [Fact]
    public void TryAdd_BeyondCapacity_ReturnsFalse()
    {
        var list = new FixedSizeList<int>(stackalloc int[1]);
        Assert.True(list.TryAdd(1));
        Assert.False(list.TryAdd(2));
    }

    [Fact]
    public void AsSpan_ReturnsCorrectSlice()
    {
        var list = new FixedSizeList<int>(stackalloc int[8]);
        list.Add(10);
        list.Add(20);
        var span = list.AsSpan();
        Assert.Equal(2, span.Length);
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL

**Step 3: Implement FixedSizeList\<T\>**

`src/ZeroAlloc.Collections/FixedSizeList.cs`:
```csharp
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A fixed-capacity list backed by a caller-provided Span (e.g. stackalloc).
/// Zero heap allocation. Use <see cref="HeapFixedSizeList{T}"/> for heap variant.
/// </summary>
public ref struct FixedSizeList<T>
{
    private readonly Span<T> _buffer;
    private int _count;

    public FixedSizeList(Span<T> buffer)
    {
        _buffer = buffer;
        _count = 0;
    }

    public readonly int Count => _count;
    public readonly int Capacity => _buffer.Length;
    public readonly bool IsFull => _count == _buffer.Length;

    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref _buffer[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_count == _buffer.Length)
            throw new InvalidOperationException("FixedSizeList is full.");
        _buffer[_count++] = item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(T item)
    {
        if (_count == _buffer.Length) return false;
        _buffer[_count++] = item;
        return true;
    }

    public readonly Span<T> AsSpan() => _buffer[.._count];
    public readonly ReadOnlySpan<T> AsReadOnlySpan() => _buffer[.._count];

    public void Clear() => _count = 0;

    public readonly Enumerator GetEnumerator() => new(_buffer[.._count]);

    public ref struct Enumerator
    {
        private readonly Span<T> _span;
        private int _index;

        internal Enumerator(Span<T> span) { _span = span; _index = -1; }
        public bool MoveNext() => ++_index < _span.Length;
        public readonly ref readonly T Current => ref _span[_index];
    }
}
```

Note: `FixedSizeList<T>` does not need `Dispose` — it doesn't own any pooled memory. The caller manages the backing `Span<T>`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Collections.Tests --verbosity normal --filter "FixedSizeListTests"`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections/FixedSizeList.cs tests/ZeroAlloc.Collections.Tests/FixedSizeListTests.cs
git commit -m "feat(fixed-size-list): add FixedSizeList<T> ref struct"
```

---

### Task 24: HeapFixedSizeList\<T\>

**Files:**
- Create: `src/ZeroAlloc.Collections/HeapFixedSizeList.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/HeapFixedSizeListTests.cs`

Class variant with fixed-size array allocated in constructor. Implements `IList<T>`, `IReadOnlyList<T>`. No pooling needed — the array lives for the object lifetime.

**Commit:** `feat(fixed-size-list): add HeapFixedSizeList<T>`

---

## Phase 9: Source Generators

### Task 25: Generator project scaffolding — marker attributes

**Files:**
- Create: `src/ZeroAlloc.Collections/Attributes/ZeroAllocListAttribute.cs`
- Create: `src/ZeroAlloc.Collections/Attributes/PooledCollectionAttribute.cs`
- Create: `src/ZeroAlloc.Collections/Attributes/ZeroAllocEnumerableAttribute.cs`

**Step 1: Create marker attributes in the core library**

```csharp
namespace ZeroAlloc.Collections;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocListAttribute<T> : Attribute { }

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class PooledCollectionAttribute<T> : Attribute { }

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocEnumerableAttribute : Attribute { }
```

**Step 2: Commit**

```bash
git add src/ZeroAlloc.Collections/Attributes/
git commit -m "feat(generators): add marker attributes for source generators"
```

---

### Task 26: ZeroAllocList source generator

**Files:**
- Create: `src/ZeroAlloc.Collections.Generators/ZeroAllocListGenerator.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/Generators/ZeroAllocListGeneratorTests.cs`

**Step 1: Write failing generator test**

Use `Microsoft.CodeAnalysis.CSharp.Testing.XUnit` to verify generated output. Add test package references:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.1.2" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" Version="1.1.2" />
```

Test that `[ZeroAllocList<int>] partial struct IntList;` generates a partial struct with `Add(int)`, `ref int this[int]`, zero-alloc enumerator, `Dispose()`.

**Step 2: Run test to verify it fails**

Expected: FAIL

**Step 3: Implement ZeroAllocListGenerator**

Incremental generator (`IIncrementalGenerator`) that:
1. Finds types with `[ZeroAllocList<T>]`
2. Emits a partial struct/class with specialized `Add(T)`, indexer, `AsSpan`, `GetEnumerator`, `Dispose`
3. Uses `[MethodImpl(AggressiveInlining)]`
4. Emits `#nullable enable`

**Step 4: Run test to verify it passes**

Expected: PASS

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Collections.Generators/ZeroAllocListGenerator.cs tests/ZeroAlloc.Collections.Tests/Generators/
git commit -m "feat(generators): add ZeroAllocList<T> source generator"
```

---

### Task 27: PooledCollection source generator

**Files:**
- Create: `src/ZeroAlloc.Collections.Generators/PooledCollectionGenerator.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/Generators/PooledCollectionGeneratorTests.cs`

Incremental generator that generates typed wrapper with `Add(T)`, `Dispose()` (returns to pool), enumerator. TDD same pattern as Task 26.

**Commit:** `feat(generators): add PooledCollection<T> source generator`

---

### Task 28: ZeroAllocEnumerable source generator

**Files:**
- Create: `src/ZeroAlloc.Collections.Generators/ZeroAllocEnumerableGenerator.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/Generators/ZeroAllocEnumerableGeneratorTests.cs`

Incremental generator that finds types with `[ZeroAllocEnumerable]`, detects `T[] _items` + `int _count` fields, emits `GetEnumerator()` returning ref struct `Enumerator`. TDD same pattern.

**Commit:** `feat(generators): add ZeroAllocEnumerable source generator`

---

### Task 29: Diagnostic analyzers

**Files:**
- Create: `src/ZeroAlloc.Collections.Generators/Diagnostics/UndisposedPooledCollectionAnalyzer.cs`
- Create: `tests/ZeroAlloc.Collections.Tests/Generators/UndisposedAnalyzerTests.cs`

Analyzer that warns when a `PooledList<T>`, `PooledStack<T>`, etc. is created but never disposed. Diagnostic ID: `ZAC001`.

**Commit:** `feat(generators): add undisposed collection analyzer ZAC001`

---

## Phase 10: Benchmarks

### Task 30: PooledList benchmarks

**Files:**
- Create: `tests/ZeroAlloc.Collections.Benchmarks/PooledListBenchmarks.cs`
- Modify: `tests/ZeroAlloc.Collections.Benchmarks/Program.cs`

**Step 1: Write benchmarks**

```csharp
using BenchmarkDotNet.Attributes;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class PooledListBenchmarks
{
    [Params(100, 1000, 10000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int List_Add()
    {
        var list = new List<int>();
        for (int i = 0; i < N; i++) list.Add(i);
        return list.Count;
    }

    [Benchmark]
    public int PooledList_Add()
    {
        using var list = new PooledList<int>();
        for (int i = 0; i < N; i++) list.Add(i);
        return list.Count;
    }

    [Benchmark]
    public int List_Enumerate()
    {
        var list = new List<int>(N);
        for (int i = 0; i < N; i++) list.Add(i);
        int sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }

    [Benchmark]
    public int PooledList_Enumerate()
    {
        using var list = new PooledList<int>(N);
        for (int i = 0; i < N; i++) list.Add(i);
        int sum = 0;
        foreach (ref readonly var item in list) sum += item;
        return sum;
    }
}
```

**Step 2: Update Program.cs**

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

**Step 3: Verify benchmark compiles**

Run: `dotnet build tests/ZeroAlloc.Collections.Benchmarks`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Collections.Benchmarks/
git commit -m "perf(benchmarks): add PooledList vs List benchmarks"
```

---

### Task 31: Remaining collection benchmarks

**Files:**
- Create: `tests/ZeroAlloc.Collections.Benchmarks/RingBufferBenchmarks.cs`
- Create: `tests/ZeroAlloc.Collections.Benchmarks/SpanDictionaryBenchmarks.cs`
- Create: `tests/ZeroAlloc.Collections.Benchmarks/PooledStackBenchmarks.cs`
- Create: `tests/ZeroAlloc.Collections.Benchmarks/PooledQueueBenchmarks.cs`

Add benchmarks comparing each collection against its BCL equivalent. Same pattern as Task 30.

**Commit:** `perf(benchmarks): add benchmarks for all collection types`

---

## Phase 11: Cleanup

### Task 32: Remove placeholder files, final build verification

**Files:**
- Delete: `src/ZeroAlloc.Collections/Placeholder.cs`
- Delete: `src/ZeroAlloc.Collections.Generators/Placeholder.cs`
- Delete: `tests/ZeroAlloc.Collections.Tests/SmokeTest.cs`

**Step 1: Delete placeholders**

```bash
rm src/ZeroAlloc.Collections/Placeholder.cs
rm src/ZeroAlloc.Collections.Generators/Placeholder.cs
rm tests/ZeroAlloc.Collections.Tests/SmokeTest.cs
```

**Step 2: Full build and test**

Run: `dotnet build ZeroAlloc.Collections.slnx --configuration Release`
Expected: Build succeeded, 0 errors

Run: `dotnet test ZeroAlloc.Collections.slnx --configuration Release --verbosity normal`
Expected: All tests pass

**Step 3: Commit**

```bash
git add -A
git commit -m "chore: remove placeholder files"
```

---

## Summary

| Phase | Tasks | Collections Covered |
|-------|-------|-------------------|
| 1. Scaffold | 1-6 | — |
| 2. PooledList (ref) | 7-11 | `PooledList<T>` |
| 3. HeapPooledList | 12 | `HeapPooledList<T>` |
| 4. RingBuffer | 13-15 | `RingBuffer<T>`, `HeapRingBuffer<T>` |
| 5. PooledStack | 16-17 | `PooledStack<T>`, `HeapPooledStack<T>` |
| 6. PooledQueue | 18-19 | `PooledQueue<T>`, `HeapPooledQueue<T>` |
| 7. SpanDictionary | 20-22 | `SpanDictionary<TKey,TValue>`, `HeapSpanDictionary<TKey,TValue>` |
| 8. FixedSizeList | 23-24 | `FixedSizeList<T>`, `HeapFixedSizeList<T>` |
| 9. Source Generators | 25-29 | Generators + analyzers |
| 10. Benchmarks | 30-31 | All collections |
| 11. Cleanup | 32 | — |

**Total: 32 tasks across 11 phases.**
