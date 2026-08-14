namespace Flow.Documents;

internal static class PresentationIntentValidation
{
    internal static void ValidateEnum<TEnum>(TEnum? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (value is not null && !Enum.IsDefined(value.Value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value is not defined.");
        }
    }

    internal static void ValidatePositiveLength(Length? length, string parameterName)
    {
        if (length is not null && length.Value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, length, "The length must be greater than zero.");
        }
    }
}
