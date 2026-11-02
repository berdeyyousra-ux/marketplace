namespace Marketplace.Domain.Common;

public readonly record struct Money
{
    public const string EuroCurrency = "EUR";

    public Money(decimal amount, string currency)
    {
        if (!IsValidCurrency(currency))
        {
            throw new ArgumentException("Currency must be three uppercase letters.", nameof(currency));
        }

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Eur(decimal amount) => new(amount, EuroCurrency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public void Deconstruct(out decimal amount, out string currency)
    {
        amount = Amount;
        currency = Currency;
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }

    private static bool IsValidCurrency(string? currency) =>
        currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper);
}
