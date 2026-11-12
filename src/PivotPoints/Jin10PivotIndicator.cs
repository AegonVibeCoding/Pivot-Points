using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;

using ATAS.Indicators;
using ATAS.Indicators.Drawing;

namespace Jin10Pivot;

public enum PivotPeriod
{
    [Display(Name = "跟随图表")]
    Chart,
    [Display(Name = "1 分钟")]
    Minute1,
    [Display(Name = "5 分钟")]
    Minute5,
    [Display(Name = "10 分钟")]
    Minute10,
    [Display(Name = "15 分钟")]
    Minute15,
    [Display(Name = "30 分钟")]
    Minute30,
    [Display(Name = "1 小时")]
    Hour1,
    [Display(Name = "2 小时")]
    Hour2,
    [Display(Name = "4 小时")]
    Hour4,
    [Display(Name = "6 小时")]
    Hour6,
    [Display(Name = "8 小时")]
    Hour8,
    [Display(Name = "12 小时")]
    Hour12,
    [Display(Name = "日")]
    Daily,
    [Display(Name = "周")]
    Weekly,
    [Display(Name = "月")]
    Monthly,
    [Display(Name = "季")]
    Quarterly
}

[DisplayName("Pivot Points")]
[Display(Description = "Classic, Woodie, Fibonacci and Camarilla pivot levels calculated from the previous completed period.")]
public sealed class Jin10PivotIndicator : Indicator
{
    private static readonly PivotMethod[] IndividualMethods =
    [
        PivotMethod.Classic,
        PivotMethod.Woodie,
        PivotMethod.Fibonacci,
        PivotMethod.Camarilla
    ];

    private readonly ValueDataSeries _styleP = CreateSeries("StyleP", "P", Color.Goldenrod, 8);
    private readonly ValueDataSeries _styleR1 = CreateSeries("StyleR1", "R1", Color.OrangeRed);
    private readonly ValueDataSeries _styleR2 = CreateSeries("StyleR2", "R2", Color.IndianRed);
    private readonly ValueDataSeries _styleR3 = CreateSeries("StyleR3", "R3", Color.Firebrick);
    private readonly ValueDataSeries _styleS1 = CreateSeries("StyleS1", "S1", Color.DodgerBlue);
    private readonly ValueDataSeries _styleS2 = CreateSeries("StyleS2", "S2", Color.RoyalBlue);
    private readonly ValueDataSeries _styleS3 = CreateSeries("StyleS3", "S3", Color.Navy);

    private readonly Dictionary<PivotMethod, PivotSeriesGroup> _seriesByMethod = [];
    private readonly List<ValueDataSeries> _allSeries = [];
    private readonly Dictionary<PivotMethod, PivotLevels> _levelsByMethod = [];

    private PivotMethod _method = PivotMethod.Classic;
    private PivotPeriod _referencePeriod = PivotPeriod.Daily;
    private WoodiePriceMode _woodiePriceMode = WoodiePriceMode.PreviousClose;
    private int _periodStartHour;
    private bool _roundToTick = true;
    private bool _onlyCurrentDay;

    private bool _hasAggregate;
    private bool _hasLevels;
    private DateTime _aggregateStart;
    private PivotOhlc _aggregate;

    [OFT.Attributes.Parameter]
    [Display(Name = "计算方法", GroupName = "计算", Order = 10)]
    public PivotMethod Method
    {
        get => _method;
        set
        {
            if (_method == value)
                return;
            _method = value;
            RecalculateValues();
        }
    }

    [OFT.Attributes.Parameter]
    [Display(Name = "参考周期", GroupName = "计算", Description = "支撑阻力使用上一完整参考周期的 OHLC。", Order = 20)]
    public PivotPeriod ReferencePeriod
    {
        get => _referencePeriod;
        set
        {
            if (_referencePeriod == value)
                return;
            _referencePeriod = value;
            RecalculateValues();
        }
    }

    [OFT.Attributes.Parameter]
    [Display(Name = "伍迪加权价", GroupName = "计算", Description = "金十对照样本采用“上一周期收盘”。", Order = 30)]
    public WoodiePriceMode WoodieMode
    {
        get => _woodiePriceMode;
        set
        {
            if (_woodiePriceMode == value)
                return;
            _woodiePriceMode = value;
            RecalculateValues();
        }
    }

    [OFT.Attributes.Parameter]
    [Range(0, 23)]
    [Display(Name = "周期起点小时", GroupName = "时间", Description = "按图表时区设置。日线默认 0 表示 00:00 换日。", Order = 40)]
    public int PeriodStartHour
    {
        get => _periodStartHour;
        set
        {
            if (_periodStartHour == value)
                return;
            _periodStartHour = value;
            RecalculateValues();
        }
    }

    [OFT.Attributes.Parameter]
    [Display(Name = "按最小跳动取整", GroupName = "显示", Order = 50)]
    public bool RoundToTick
    {
        get => _roundToTick;
        set
        {
            if (_roundToTick == value)
                return;
            _roundToTick = value;
            RecalculateValues();
        }
    }

    [OFT.Attributes.Parameter]
    [Display(Name = "仅显示当日", GroupName = "显示", Description = "开启后只绘制当前交易日，交易日边界跟随“周期起点小时”。", Order = 60)]
    public bool OnlyCurrentDay
    {
        get => _onlyCurrentDay;
        set
        {
            if (_onlyCurrentDay == value)
                return;
            _onlyCurrentDay = value;
            RecalculateValues();
        }
    }

    public Jin10PivotIndicator()
    {
        Panel = IndicatorDataProvider.CandlesPanel;
        DenyToChangePanel = true;

        DataSeries[0] = _styleP;
        DataSeries.Add(_styleR1);
        DataSeries.Add(_styleR2);
        DataSeries.Add(_styleR3);
        DataSeries.Add(_styleS1);
        DataSeries.Add(_styleS2);
        DataSeries.Add(_styleS3);

        AddMethodSeries(PivotMethod.Classic, "Classic", "经典");
        AddMethodSeries(PivotMethod.Woodie, "Woodie", "伍迪");
        AddMethodSeries(PivotMethod.Fibonacci, "Fibonacci", "斐波那契");
        AddMethodSeries(PivotMethod.Camarilla, "Camarilla", "卡玛利拉");

        BindStyle(_styleP, group => group.Pivot);
        BindStyle(_styleR1, group => group.R1);
        BindStyle(_styleR2, group => group.R2);
        BindStyle(_styleR3, group => group.R3);
        BindStyle(_styleS1, group => group.S1);
        BindStyle(_styleS2, group => group.S2);
        BindStyle(_styleS3, group => group.S3);
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
            ResetState();

        var candle = GetCandle(bar);
        var periodStart = GetPeriodStart(candle.Time, bar);

        if (!_hasAggregate)
        {
            StartAggregate(periodStart, candle);
            ClearBar(bar);
            return;
        }

        if (periodStart != _aggregateStart)
        {
            CalculateLevels(candle.Open);
            _hasLevels = true;

            BreakLines(bar - 1);
            StartAggregate(periodStart, candle);
        }
        else
        {
            _aggregate = _aggregate with
            {
                High = Math.Max(_aggregate.High, candle.High),
                Low = Math.Min(_aggregate.Low, candle.Low),
                Close = candle.Close
            };
        }

        if (!_hasLevels)
        {
            ClearBar(bar);
            return;
        }

        if (OnlyCurrentDay && !IsCurrentTradingDay(candle.Time))
        {
            ClearBar(bar);
            return;
        }

        WriteBar(bar);
    }

    private void ResetState()
    {
        _hasAggregate = false;
        _hasLevels = false;
        _aggregateStart = default;
        _aggregate = default;
        _levelsByMethod.Clear();

        foreach (var series in _allSeries)
            series.Clear();
    }

    private void StartAggregate(DateTime periodStart, IndicatorCandle candle)
    {
        _aggregateStart = periodStart;
        _aggregate = new PivotOhlc(candle.Open, candle.High, candle.Low, candle.Close);
        _hasAggregate = true;
    }

    private void CalculateLevels(decimal currentOpen)
    {
        _levelsByMethod.Clear();

        if (Method == PivotMethod.All)
        {
            foreach (var method in IndividualMethods)
                _levelsByMethod[method] = PivotMath.Calculate(method, _aggregate, currentOpen, WoodieMode);

            return;
        }

        _levelsByMethod[Method] = PivotMath.Calculate(Method, _aggregate, currentOpen, WoodieMode);
    }

    private void WriteBar(int bar)
    {
        foreach (var pair in _seriesByMethod)
        {
            if (!_levelsByMethod.TryGetValue(pair.Key, out var value))
            {
                pair.Value.ClearBar(bar);
                continue;
            }

            var group = pair.Value;
            group.R3[bar] = Normalize(value.R3);
            group.R2[bar] = Normalize(value.R2);
            group.R1[bar] = Normalize(value.R1);
            group.Pivot[bar] = Normalize(value.Pivot);
            group.S1[bar] = Normalize(value.S1);
            group.S2[bar] = Normalize(value.S2);
            group.S3[bar] = Normalize(value.S3);
        }
    }

    private void ClearBar(int bar)
    {
        foreach (var series in _allSeries)
            series[bar] = 0m;
    }

    private void BreakLines(int bar)
    {
        if (bar < 0)
            return;

        foreach (var series in _allSeries)
            series.SetPointOfEndLine(bar);
    }

    private decimal Normalize(decimal value)
    {
        if (!RoundToTick || InstrumentInfo is null || InstrumentInfo.TickSize <= 0)
            return value;

        var tick = InstrumentInfo.TickSize;
        return Math.Round(value / tick, 0, MidpointRounding.AwayFromZero) * tick;
    }

    private bool IsCurrentTradingDay(DateTime time)
    {
        if (CurrentBar <= 0)
            return false;

        var lastCandle = GetCandle(CurrentBar - 1);
        return GetTradingDate(time) == GetTradingDate(lastCandle.Time);
    }

    private DateTime GetTradingDate(DateTime time) =>
        time.AddHours(-PeriodStartHour).Date;

    private DateTime GetPeriodStart(DateTime time, int bar)
    {
        if (ReferencePeriod == PivotPeriod.Chart)
            return time;

        var shifted = time.AddHours(-PeriodStartHour);
        DateTime start;

        switch (ReferencePeriod)
        {
            case PivotPeriod.Daily:
                start = shifted.Date;
                break;
            case PivotPeriod.Weekly:
                var daysFromMonday = ((int)shifted.DayOfWeek + 6) % 7;
                start = shifted.Date.AddDays(-daysFromMonday);
                break;
            case PivotPeriod.Monthly:
                start = new DateTime(shifted.Year, shifted.Month, 1, 0, 0, 0, shifted.Kind);
                break;
            case PivotPeriod.Quarterly:
                var firstMonth = ((shifted.Month - 1) / 3) * 3 + 1;
                start = new DateTime(shifted.Year, firstMonth, 1, 0, 0, 0, shifted.Kind);
                break;
            default:
                var duration = GetIntradayDuration(ReferencePeriod);
                start = new DateTime(
                    shifted.Ticks / duration.Ticks * duration.Ticks,
                    shifted.Kind);
                break;
        }

        return start.AddHours(PeriodStartHour);
    }

    private static TimeSpan GetIntradayDuration(PivotPeriod period) => period switch
    {
        PivotPeriod.Minute1 => TimeSpan.FromMinutes(1),
        PivotPeriod.Minute5 => TimeSpan.FromMinutes(5),
        PivotPeriod.Minute10 => TimeSpan.FromMinutes(10),
        PivotPeriod.Minute15 => TimeSpan.FromMinutes(15),
        PivotPeriod.Minute30 => TimeSpan.FromMinutes(30),
        PivotPeriod.Hour1 => TimeSpan.FromHours(1),
        PivotPeriod.Hour2 => TimeSpan.FromHours(2),
        PivotPeriod.Hour4 => TimeSpan.FromHours(4),
        PivotPeriod.Hour6 => TimeSpan.FromHours(6),
        PivotPeriod.Hour8 => TimeSpan.FromHours(8),
        PivotPeriod.Hour12 => TimeSpan.FromHours(12),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null)
    };

    private static ValueDataSeries CreateSeries(
        string id,
        string name,
        Color color,
        int width = 3) =>
        new(id, name)
        {
            Color = color.Convert(),
            VisualType = VisualMode.Line,
            Width = width,
            ShowZeroValue = false,
            ShowCurrentValue = true
        };

    private void AddMethodSeries(
        PivotMethod method,
        string idPrefix,
        string displayPrefix)
    {
        var group = new PivotSeriesGroup(idPrefix, displayPrefix);
        _seriesByMethod.Add(method, group);

        foreach (var series in group.All)
        {
            series.IsHidden = true;
            DataSeries.Add(series);
            _allSeries.Add(series);
        }
    }

    private void BindStyle(
        ValueDataSeries template,
        Func<PivotSeriesGroup, ValueDataSeries> selector)
    {
        void Apply()
        {
            foreach (var group in _seriesByMethod.Values)
                CopyStyle(template, selector(group));
        }

        template.PropertyChanged += (_, _) => Apply();
        Apply();
    }

    private static void CopyStyle(ValueDataSeries source, ValueDataSeries target)
    {
        target.Color = source.Color;
        target.Width = source.Width;
        target.LineDashStyle = source.LineDashStyle;
        target.VisualType = source.VisualType;
        target.ShowCurrentValue = source.ShowCurrentValue;
        target.ShowZeroValue = source.ShowZeroValue;
        target.ScaleIt = source.ScaleIt;
        target.DrawAbovePrice = source.DrawAbovePrice;
        target.ShowTooltip = source.ShowTooltip;
        target.ShowNameOnMouseOver = source.ShowNameOnMouseOver;
    }

    private sealed class PivotSeriesGroup
    {
        public ValueDataSeries R3 { get; }
        public ValueDataSeries R2 { get; }
        public ValueDataSeries R1 { get; }
        public ValueDataSeries Pivot { get; }
        public ValueDataSeries S1 { get; }
        public ValueDataSeries S2 { get; }
        public ValueDataSeries S3 { get; }
        public ValueDataSeries[] All { get; }

        public PivotSeriesGroup(string idPrefix, string displayPrefix)
        {
            R3 = CreateSeries($"{idPrefix}R3", $"{displayPrefix} R3", Color.Gray);
            R2 = CreateSeries($"{idPrefix}R2", $"{displayPrefix} R2", Color.Gray);
            R1 = CreateSeries($"{idPrefix}R1", $"{displayPrefix} R1", Color.Gray);
            Pivot = CreateSeries($"{idPrefix}Pivot", $"{displayPrefix} P", Color.Gray, 8);
            S1 = CreateSeries($"{idPrefix}S1", $"{displayPrefix} S1", Color.Gray);
            S2 = CreateSeries($"{idPrefix}S2", $"{displayPrefix} S2", Color.Gray);
            S3 = CreateSeries($"{idPrefix}S3", $"{displayPrefix} S3", Color.Gray);
            All = [R3, R2, R1, Pivot, S1, S2, S3];
        }

        public void ClearBar(int bar)
        {
            foreach (var series in All)
                series[bar] = 0m;
        }
    }
}
