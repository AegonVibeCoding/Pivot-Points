using System;
using System.ComponentModel.DataAnnotations;

namespace PivotPoints;

public enum PivotMethod
{
    [Display(Name = "全部")]
    All = 4,
    [Display(Name = "经典")]
    Classic = 0,
    [Display(Name = "伍迪")]
    Woodie = 1,
    [Display(Name = "斐波那契")]
    Fibonacci = 2,
    [Display(Name = "卡玛利拉")]
    Camarilla = 3
}

public enum WoodiePriceMode
{
    [Display(Name = "上一周期收盘")]
    PreviousClose,
    [Display(Name = "当前周期开盘")]
    CurrentOpen
}

public readonly record struct PivotOhlc(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);

public readonly record struct PivotLevels(
    decimal R3,
    decimal R2,
    decimal R1,
    decimal Pivot,
    decimal S1,
    decimal S2,
    decimal S3);

public static class PivotMath
{
    public static PivotLevels Calculate(
        PivotMethod method,
        PivotOhlc previous,
        decimal currentOpen,
        WoodiePriceMode woodiePriceMode = WoodiePriceMode.PreviousClose)
    {
        if (previous.High < previous.Low)
            throw new ArgumentOutOfRangeException(nameof(previous), "High cannot be lower than Low.");

        return method switch
        {
            PivotMethod.Classic => Classic(previous),
            PivotMethod.Woodie => Woodie(previous, currentOpen, woodiePriceMode),
            PivotMethod.Fibonacci => Fibonacci(previous),
            PivotMethod.Camarilla => Camarilla(previous),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    // Traditional pivot formula, displayed as “经典” in the indicator.
    public static PivotLevels Classic(PivotOhlc value)
    {
        var p = (value.High + value.Low + value.Close) / 3m;
        var range = value.High - value.Low;

        return new PivotLevels(
            value.High + 2m * (p - value.Low),
            p + range,
            2m * p - value.Low,
            p,
            2m * p - value.High,
            p - range,
            value.Low - 2m * (value.High - p));
    }

    public static PivotLevels Woodie(
        PivotOhlc value,
        decimal currentOpen,
        WoodiePriceMode priceMode = WoodiePriceMode.PreviousClose)
    {
        var weightedPrice = priceMode == WoodiePriceMode.PreviousClose
            ? value.Close
            : currentOpen;
        var p = (value.High + value.Low + 2m * weightedPrice) / 4m;
        var range = value.High - value.Low;

        return new PivotLevels(
            value.High + 2m * (p - value.Low),
            p + range,
            2m * p - value.Low,
            p,
            2m * p - value.High,
            p - range,
            value.Low - 2m * (value.High - p));
    }

    public static PivotLevels Fibonacci(PivotOhlc value)
    {
        var p = (value.High + value.Low + value.Close) / 3m;
        var range = value.High - value.Low;

        return new PivotLevels(
            p + range,
            p + 0.618m * range,
            p + 0.382m * range,
            p,
            p - 0.382m * range,
            p - 0.618m * range,
            p - range);
    }

    // Publicly documented, standard Camarilla R1-R3 / S1-S3 formula.
    public static PivotLevels Camarilla(PivotOhlc value)
    {
        var p = (value.High + value.Low + value.Close) / 3m;
        var range = value.High - value.Low;
        var factor = 1.1m * range;

        return new PivotLevels(
            value.Close + factor / 4m,
            value.Close + factor / 6m,
            value.Close + factor / 12m,
            p,
            value.Close - factor / 12m,
            value.Close - factor / 6m,
            value.Close - factor / 4m);
    }
}
