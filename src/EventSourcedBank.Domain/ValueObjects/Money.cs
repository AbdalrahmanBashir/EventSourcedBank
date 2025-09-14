namespace EventSourcedBank.Domain.ValueObjects
{
     // Represents a monetary value, including an amount and a currency code.
    
    public record Money(decimal Amount, string Currency)
    {
        public Money Add(Money other)
        {
            if (other.Currency != Currency)
                throw new InvalidOperationException("Cannot add money with different currencies.");
            return this with { Amount = Amount + other.Amount };
        }
        public Money Subtract(Money other)
        {
            if (other.Currency != Currency)
                throw new InvalidOperationException("Cannot subtract money with different currencies.");
            return this with { Amount = Amount - other.Amount };
        }
    }
}
