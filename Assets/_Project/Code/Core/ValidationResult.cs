namespace DestinyTogether.Core
{
    /// <summary>
    /// Resultado tipado de validacao de comando. A mesma validacao roda no cliente
    /// (feedback imediato na UI) e no servidor (autoridade) — por isso vive na camada pura.
    /// </summary>
    public readonly struct ValidationResult
    {
        public readonly bool IsValid;
        public readonly string Reason;

        private ValidationResult(bool valid, string reason)
        {
            IsValid = valid;
            Reason = reason;
        }

        public static readonly ValidationResult Ok = new ValidationResult(true, null);
        public static ValidationResult Fail(string reason) => new ValidationResult(false, reason);

        public override string ToString() => IsValid ? "OK" : $"INVALIDO: {Reason}";
    }
}
