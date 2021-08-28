using System;
using System.Collections.Generic;
using System.Text;

namespace Sunlighter.OptionLib
{
    public abstract class Option<T>
    {
        private static readonly OptNone none = new OptNone();

        public static Option<T> None => none;

        public static Option<T> Some(T value) => new OptSome(value);

        public abstract bool HasValue { get; }
        public abstract T Value { get; }

        private sealed class OptSome : Option<T>
        {
            private readonly T value;

            public OptSome(T value)
            {
                this.value = value;
            }

            public override bool HasValue { get { return true; } }
            public override T Value { get { return value; } }
        }

        private sealed class OptNone : Option<T>
        {
            public override bool HasValue { get { return false; } }
            public override T Value { get { throw new InvalidOperationException(); } }
        }
    }

    public static partial class Extensions
    {
        public static Option<U> Map<T, U>(this Option<T> opt, Func<T, U> func)
        {
            if (opt.HasValue)
            {
                return Option<U>.Some(func(opt.Value));
            }
            else
            {
                return Option<U>.None;
            }
        }
    }
}
