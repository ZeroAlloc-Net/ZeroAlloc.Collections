using ZeroAlloc.Collections;

// Uses members the bundled generator emits. If the compiler skips the generator, this does not build.
var list = new IntList(capacity: 4);
try
{
    for (var i = 0; i < 10; i++)
    {
        list.Add(i);
    }

    if (list.Count != 10 || list.AsReadOnlySpan()[9] != 9)
    {
        Console.Error.WriteLine("SDK 10 consumer: the generated list returned wrong data.");
        return 1;
    }
}
finally
{
    list.Dispose();
}

Console.WriteLine("SDK 10 consumer: generator loaded and the generated list works.");
return 0;

[ZeroAllocList(typeof(int))]
public partial struct IntList;
