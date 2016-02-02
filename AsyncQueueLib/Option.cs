using System;

namespace Sunlighter.AsyncQueueLib
{
    public abstract class Option<T>
    {
        private bool hasValue;

        protected Option(bool hasValue)
        {
            this.hasValue = hasValue;
        }

        public bool HasValue { get { return hasValue; } }

        public abstract T Value { get; }
    }

    public sealed class Some<T> : Option<T>
    {
        private T value;

        public Some(T value) : base(true)
        {
            this.value = value;
        }

        public override T Value { get { return value; } }
    }

    public sealed class None<T> : Option<T>
    {
        public None() : base(false)
        {

        }

        public override T Value { get { throw new InvalidOperationException(); } }
    }
}
