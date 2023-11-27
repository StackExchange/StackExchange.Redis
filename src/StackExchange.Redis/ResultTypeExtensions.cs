namespace StackExchange.Redis
{
    internal static class ResultTypeExtensions
    {
        public static bool IsError(this ResultType value)
            => (value & (ResultType)0b111) == ResultType.Error;

        public static ResultType ToResp2(this ResultType value)
            => value & (ResultType)0b111; // just keep the last 3 bits
    }
}
