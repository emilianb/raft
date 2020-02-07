namespace Raft.Components
{
    public class Result<T>
    {
        public Result(T value, int term)
        {
            Value = value;
            Term = term;
        }

        public T Value { get; }

        public int Term { get; }
    }
}
