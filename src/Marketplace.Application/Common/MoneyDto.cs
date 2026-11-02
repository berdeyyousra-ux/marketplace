using System.Globalization;
using FluentValidation;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Common;

// Marks a validator that is only used inside another validator (SetValidator). It is not registered in DI,
// so it never runs on its own against an action argument of its type.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ComponentValidatorAttribute : Attribute;

// D-11: money in the API is { "amount": "19.99", "currency": "EUR" }, with the amount as a string.
public sealed record MoneyDto(string Amount, string Currency)
{
    public static MoneyDto From(Money money) => new(money.Amount.ToString("0.00", CultureInfo.InvariantCulture), money.Currency);

    public bool TryToMoney(out Money money)
    {
        money = default;
        if (!decimal.TryParse(Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || decimal.Round(amount, 2) != amount
            || Currency is not { Length: 3 } || !Currency.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        money = new Money(amount, Currency);
        return true;
    }
}

[ComponentValidator]
public sealed class MoneyDtoValidator : AbstractValidator<MoneyDto>
{
    public MoneyDtoValidator(decimal min, decimal max)
    {
        RuleFor(money => money).Must(money => money.TryToMoney(out _))
            .WithMessage("Amount must be a decimal string with up to 2 decimal places, such as \"19.99\", and currency a 3-letter code.");
        RuleFor(money => money.Currency).Equal(Money.EuroCurrency).WithMessage("Only EUR is supported.");
        RuleFor(money => money).Must(money => money.TryToMoney(out var parsed) && parsed.Amount >= min && parsed.Amount <= max)
            .WithMessage(string.Create(CultureInfo.InvariantCulture, $"Amount must be between {min:0.00} and {max:0.00}."));
    }
}
