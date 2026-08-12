namespace S3.EditorInterop.Server;

/// <summary>Minimal assertion harness so the spike reports pass/fail rather than needing a reader.</summary>
public static class Check
{
    private static int _passed;
    private static int _failed;

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
    }

    public static void That(bool condition, string description, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {description}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {description}");
        }

        if (detail is not null)
        {
            Console.WriteLine($"        {detail}");
        }
    }

    public static void Note(string text) => Console.WriteLine($"        {text}");

    public static int Summarize()
    {
        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed.");

        return _failed == 0 ? 0 : 1;
    }
}
