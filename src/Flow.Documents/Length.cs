namespace Flow.Documents;

public readonly record struct Length
{
    private Length(double value, LengthUnit unit)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A length must be finite.");
        }

        Value = value;
        Unit = unit;
    }

    public double Value { get; }

    public LengthUnit Unit { get; }

    public static Length Px(double value) => new(value, LengthUnit.Pixel);

    public static Length Rem(double value) => new(value, LengthUnit.RootEm);

    public static Length Em(double value) => new(value, LengthUnit.Em);

    public static Length Percent(double value) => new(value, LengthUnit.Percent);
}

public enum LengthUnit
{
    Pixel,
    RootEm,
    Em,
    Percent,
}
