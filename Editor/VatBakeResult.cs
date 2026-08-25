namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Outcome of a bake: what was produced, or why nothing was.
    /// </summary>
    internal sealed class VatBakeResult
    {
        private VatBakeResult(bool isSuccess, string message, VatAnimationSet set)
        {
            IsSuccess = isSuccess;
            Message = message;
            Set = set;
        }

        public bool IsSuccess { get; }
        public string Message { get; }
        public VatAnimationSet Set { get; }

        public static VatBakeResult Failed(string message)
        {
            return new VatBakeResult(false, message, null);
        }

        public static VatBakeResult Succeeded(VatAnimationSet set, string message)
        {
            return new VatBakeResult(true, message, set);
        }
    }
}
