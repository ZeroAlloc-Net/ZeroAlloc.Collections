# Cookbook: Zero-Alloc Request Parsing

This recipe shows how to parse HTTP query parameters and CSV rows into typed collections without per-request heap allocations, using `SpanDictionary<TKey, TValue>` for O(1) lookup and `PooledList<T>` for accumulating parsed results.

## The Problem

In a high-throughput API handling 50k+ requests/second, each request allocates:

- A `Dictionary<string, string>` for query parameters (~2-4 allocations for buckets + entries)
- A `List<T>` for collecting parsed rows or results (~1-2 allocations for the array)
- String allocations for each parsed key/value

At 50k req/sec, this produces hundreds of thousands of short-lived objects per second, driving frequent Gen0 collections and inflating p99 tail latency.

## The Solution

| BCL                             | ZeroAlloc                          | Why                                    |
|---------------------------------|------------------------------------|----------------------------------------|
| `Dictionary<string, string>`    | `SpanDictionary<string, string>`   | No per-node allocation, open addressing |
| `List<T>` for parsed results    | `PooledList<T>`                    | ArrayPool-backed, returned on Dispose  |

`SpanDictionary` uses open addressing with linear probing in a flat array -- no linked-list nodes, no separate bucket arrays. Combined with `PooledList` for result accumulation, the entire parsing pipeline allocates nothing on the hot path.

## Example 1: Parsing Query Parameters

```csharp
using ZeroAlloc.Collections;

public readonly record struct ProductFilter(
    string? Category,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page,
    int PageSize
);

public static class QueryParser
{
    /// <summary>
    /// Parses a query string like "?category=electronics&minPrice=10&page=2"
    /// into a strongly-typed filter. Zero heap allocations for the dictionary.
    /// </summary>
    public static ProductFilter ParseProductFilter(ReadOnlySpan<char> queryString)
    {
        // SpanDictionary uses open addressing — no node allocations
        using var parameters = new SpanDictionary<string, string>(capacity: 8);

        ParseQueryString(queryString, ref parameters);

        return new ProductFilter(
            Category: parameters.TryGetValue("category", out var cat) ? cat : null,
            MinPrice: parameters.TryGetValue("minPrice", out var minP) && decimal.TryParse(minP, out var min) ? min : null,
            MaxPrice: parameters.TryGetValue("maxPrice", out var maxP) && decimal.TryParse(maxP, out var max) ? max : null,
            Page: parameters.TryGetValue("page", out var pg) && int.TryParse(pg, out var page) ? page : 1,
            PageSize: parameters.TryGetValue("pageSize", out var ps) && int.TryParse(ps, out var size) ? size : 20
        );
    }

    private static void ParseQueryString(ReadOnlySpan<char> query, ref SpanDictionary<string, string> dict)
    {
        // Skip leading '?'
        if (query.Length > 0 && query[0] == '?')
            query = query[1..];

        while (query.Length > 0)
        {
            // Find next '&' delimiter
            int ampIndex = query.IndexOf('&');
            ReadOnlySpan<char> pair = ampIndex >= 0 ? query[..ampIndex] : query;
            query = ampIndex >= 0 ? query[(ampIndex + 1)..] : ReadOnlySpan<char>.Empty;

            // Split on '='
            int eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            string key = pair[..eqIndex].ToString();
            string value = pair[(eqIndex + 1)..].ToString();
            dict[key] = value;
        }
    }
}
```

## Example 2: Parsing CSV Rows

```csharp
using ZeroAlloc.Collections;

public readonly record struct TradeRecord(
    string Symbol,
    decimal Price,
    int Quantity,
    long TimestampTicks
);

public static class CsvParser
{
    /// <summary>
    /// Parses CSV text into a list of TradeRecords.
    /// Uses PooledList to accumulate results — buffer returned on Dispose.
    /// Uses SpanDictionary to map CSV headers to column indices.
    /// </summary>
    public static TradeRecord[] ParseTrades(ReadOnlySpan<char> csvText)
    {
        using var results = new PooledList<TradeRecord>(capacity: 256);

        // Parse header row to build column index map
        int firstNewline = csvText.IndexOf('\n');
        if (firstNewline < 0) return Array.Empty<TradeRecord>();

        using var columnMap = new SpanDictionary<string, int>(capacity: 8);
        ParseHeader(csvText[..firstNewline], ref columnMap);

        // Validate required columns exist
        if (!columnMap.ContainsKey("symbol") ||
            !columnMap.ContainsKey("price") ||
            !columnMap.ContainsKey("qty") ||
            !columnMap.ContainsKey("timestamp"))
        {
            throw new FormatException("CSV missing required columns: symbol, price, qty, timestamp");
        }

        int symIdx = columnMap["symbol"];
        int priceIdx = columnMap["price"];
        int qtyIdx = columnMap["qty"];
        int tsIdx = columnMap["timestamp"];

        // Parse data rows
        ReadOnlySpan<char> remaining = csvText[(firstNewline + 1)..];

        while (remaining.Length > 0)
        {
            int newline = remaining.IndexOf('\n');
            ReadOnlySpan<char> line = newline >= 0 ? remaining[..newline] : remaining;
            remaining = newline >= 0 ? remaining[(newline + 1)..] : ReadOnlySpan<char>.Empty;

            if (line.IsEmpty || line.IsWhiteSpace()) continue;

            if (TryParseRow(line, symIdx, priceIdx, qtyIdx, tsIdx, out var trade))
            {
                results.Add(trade);
            }
        }

        return results.ToArray();
    }

    private static void ParseHeader(ReadOnlySpan<char> headerLine, ref SpanDictionary<string, int> map)
    {
        int colIndex = 0;
        while (headerLine.Length > 0)
        {
            int comma = headerLine.IndexOf(',');
            ReadOnlySpan<char> col = comma >= 0 ? headerLine[..comma] : headerLine;
            headerLine = comma >= 0 ? headerLine[(comma + 1)..] : ReadOnlySpan<char>.Empty;

            map[col.Trim().ToString().ToLowerInvariant()] = colIndex++;
        }
    }

    private static bool TryParseRow(
        ReadOnlySpan<char> line,
        int symIdx, int priceIdx, int qtyIdx, int tsIdx,
        out TradeRecord trade)
    {
        trade = default;

        // Split into fields using PooledList — returned after this scope
        using var fields = new PooledList<Range>(capacity: 8);
        int start = 0;
        for (int i = 0; i <= line.Length; i++)
        {
            if (i == line.Length || line[i] == ',')
            {
                fields.Add(start..i);
                start = i + 1;
            }
        }

        int maxIdx = Math.Max(Math.Max(symIdx, priceIdx), Math.Max(qtyIdx, tsIdx));
        if (fields.Count <= maxIdx) return false;

        ReadOnlySpan<char> symbolSpan = line[fields[symIdx]];
        ReadOnlySpan<char> priceSpan = line[fields[priceIdx]];
        ReadOnlySpan<char> qtySpan = line[fields[qtyIdx]];
        ReadOnlySpan<char> tsSpan = line[fields[tsIdx]];

        if (!decimal.TryParse(priceSpan, out var price)) return false;
        if (!int.TryParse(qtySpan, out var qty)) return false;
        if (!long.TryParse(tsSpan, out var ts)) return false;

        trade = new TradeRecord(symbolSpan.ToString(), price, qty, ts);
        return true;
    }
}
```

## Usage in an ASP.NET Core Endpoint

```csharp
app.MapGet("/products", (HttpContext ctx) =>
{
    // Parse query string with zero dictionary allocations
    var filter = QueryParser.ParseProductFilter(ctx.Request.QueryString.Value);
    var products = _repository.Query(filter);
    return Results.Ok(products);
});

app.MapPost("/trades/import", async (HttpContext ctx) =>
{
    // Read CSV body
    using var reader = new StreamReader(ctx.Request.Body);
    var csvText = await reader.ReadToEndAsync();

    // Parse with pooled collections
    var trades = CsvParser.ParseTrades(csvText.AsSpan());
    await _tradeRepository.BulkInsertAsync(trades);

    return Results.Ok(new { Imported = trades.Length });
});
```

## How SpanDictionary Differs from Dictionary

```
Dictionary<K,V>:
  ┌─────────┐     ┌──────┐     ┌──────┐
  │ buckets[]├────►│ Node ├────►│ Node │   ← each node is a heap allocation
  └─────────┘     └──────┘     └──────┘

SpanDictionary<K,V>:
  ┌──────────────────────────────────┐
  │ Entry[] (flat, open addressing)  │   ← single array, no node allocations
  │ [key|value|hash|state]           │
  │ [key|value|hash|state]           │
  │ [key|value|hash|state]           │
  └──────────────────────────────────┘
```

`SpanDictionary` uses open addressing with linear probing. Entries are stored inline in a flat array. There are no linked-list nodes, no separate bucket arrays, and no per-entry heap allocations.

## Key Takeaways

- **`SpanDictionary<TKey, TValue>`** replaces `Dictionary<TKey, TValue>` for short-lived lookups. It uses open addressing with linear probing in a flat array -- zero per-node allocations.
- **`PooledList<T>`** accumulates parsed results. When you need to return the data, call `ToArray()` once -- this is the only allocation in the pipeline.
- Both types implement `Dispose()` for cleanup. Use `using var` to ensure buffers are returned.
- For query string parsing, pre-size the `SpanDictionary` capacity to the expected number of parameters (typically 4-8) to avoid rehashing.
- `SpanDictionary` supports `TryGetValue`, indexer `[]`, `ContainsKey`, `Remove`, and `foreach` enumeration -- the same API surface as `Dictionary<TKey, TValue>` for common operations.

## Related

- [High-Throughput Message Processing](01-message-processing.md)
- [Game Loop Object Pooling](02-game-loop.md)
- [Custom Collection with Source Generator](04-custom-collection.md)
