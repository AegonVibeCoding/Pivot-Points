using Jin10Pivot;

var checks = 0;
void Equal(string name, PivotLevels actual, PivotLevels expected)
{
    if (actual != expected)
        throw new InvalidOperationException($"{name}: {actual} != {expected}");
    checks++;
    Console.WriteLine($"PASS {name}");
}
static PivotLevels Round(PivotLevels v) => new(
    decimal.Round(v.R3, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.R2, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.R1, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.Pivot, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.S1, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.S2, 2, MidpointRounding.AwayFromZero),
    decimal.Round(v.S3, 2, MidpointRounding.AwayFromZero));

var simple = new PivotOhlc(99m, 110m, 90m, 100m);
var standard = new PivotLevels(130m, 120m, 110m, 100m, 90m, 80m, 70m);
Equal("Classic hand calculation", PivotMath.Calculate(PivotMethod.Classic, simple, 104m), standard);
Equal("Woodie previous close", PivotMath.Calculate(PivotMethod.Woodie, simple, 104m), standard);
Equal("Woodie current open", PivotMath.Calculate(PivotMethod.Woodie, simple, 104m, WoodiePriceMode.CurrentOpen),
    new(134m, 122m, 114m, 102m, 94m, 82m, 74m));
Equal("Fibonacci hand calculation", PivotMath.Calculate(PivotMethod.Fibonacci, simple, 104m),
    new(120m, 112.36m, 107.64m, 100m, 92.36m, 87.64m, 80m));
Equal("Camarilla hand calculation (2dp)", Round(PivotMath.Calculate(PivotMethod.Camarilla, simple, 104m)),
    new(105.50m, 103.67m, 101.83m, 100m, 98.17m, 96.33m, 94.50m));

var screenshot = new PivotOhlc(4448.10m, 4461.85m, 4322.89m, 4328.78m);
Equal("Screenshot Classic regression", Round(PivotMath.Calculate(PivotMethod.Classic, screenshot, 4333.12m)),
    new(4558.42m, 4510.13m, 4419.46m, 4371.17m, 4280.50m, 4232.21m, 4141.54m));
Equal("Screenshot Woodie close regression", Round(PivotMath.Calculate(PivotMethod.Woodie, screenshot, 4333.12m)),
    new(4537.22m, 4499.54m, 4398.26m, 4360.58m, 4259.30m, 4221.62m, 4120.34m));
Equal("Screenshot Fibonacci regression", Round(PivotMath.Calculate(PivotMethod.Fibonacci, screenshot, 4333.12m)),
    new(4510.13m, 4457.05m, 4424.26m, 4371.17m, 4318.09m, 4285.30m, 4232.21m));

foreach (var method in new[] { PivotMethod.Classic, PivotMethod.Woodie, PivotMethod.Fibonacci, PivotMethod.Camarilla })
{
    Equal($"{method} flat input", PivotMath.Calculate(method, new(100m,100m,100m,100m), 100m),
        new(100m,100m,100m,100m,100m,100m,100m));
    if (method != PivotMethod.Woodie)
        Equal($"{method} ignores current open", PivotMath.Calculate(method, simple, 999m),
            PivotMath.Calculate(method, simple, 104m));
}

void Throws(string name, Action action)
{
    try { action(); }
    catch (ArgumentOutOfRangeException) { checks++; Console.WriteLine($"PASS {name}"); return; }
    throw new InvalidOperationException($"{name}: expected ArgumentOutOfRangeException");
}
Throws("Reject H<L", () => PivotMath.Calculate(PivotMethod.Classic, new(100m, 90m, 110m, 100m), 100m));
Throws("All must be dispatched by indicator", () => PivotMath.Calculate(PivotMethod.All, simple, 104m));
Console.WriteLine($"All {checks} checks passed. Formula checks only; ATAS chart integration is not exercised.");
