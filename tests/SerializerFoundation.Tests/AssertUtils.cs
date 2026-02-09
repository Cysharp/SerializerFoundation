using System.Runtime.CompilerServices;
using TUnit.Assertions.Conditions;
using TUnit.Assertions.Core;
using TUnit.Assertions.Enums;

namespace SerializerFoundation.Tests;

public static class AssertUtils
{
    // sync assertion for test "ref struct" that can not across "await" boundary
    extension<T>(T actual)
    {
        public void IsEqualTo(T expected, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        {
            Core(actual, expected, expectedExpression).GetAwaiter().GetResult();

            static async ValueTask Core(T actual, T expected, string? expectedExpression)
            {
                await Assert.That(actual).IsEqualTo(expected, expectedExpression!);
            }
        }

        public void IsTypeOf<TExpected>()
        {
            Core(actual).GetAwaiter().GetResult();

            static async ValueTask Core(T actual)
            {
                await Assert.That(actual).IsTypeOf<TExpected>();
            }
        }
    }

    public static void IsGreaterThan<TValue>(this TValue actual, TValue minimum, [CallerArgumentExpression(nameof(minimum))] string? minimumExpression = null)
        where TValue : System.IComparable<TValue>
    {
        Core(actual, minimum, minimumExpression).GetAwaiter().GetResult();

        static async ValueTask Core(TValue actual, TValue minimum, string? minimumExpression)
        {
            await Assert.That(actual).IsGreaterThan(minimum, minimumExpression!);
        }
    }

    public static void IsGreaterThanOrEqualTo<TValue>(this TValue actual, TValue minimum, [CallerArgumentExpression(nameof(minimum))] string? minimumExpression = null)
        where TValue : System.IComparable<TValue>
    {
        Core(actual, minimum, minimumExpression).GetAwaiter().GetResult();

        static async ValueTask Core(TValue actual, TValue minimum, string? minimumExpression)
        {
            await Assert.That(actual).IsGreaterThanOrEqualTo(minimum, minimumExpression!);
        }
    }

    public static void IsEquivalentTo<TCollection, TItem>(this TCollection actual, System.Collections.Generic.IEnumerable<TItem> expected, CollectionOrdering ordering = CollectionOrdering.Any, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null, [CallerArgumentExpression(nameof(ordering))] string? orderingExpression = null)
        where TCollection : System.Collections.Generic.IEnumerable<TItem>
    {
        Core(actual, expected, ordering, expectedExpression, orderingExpression).GetAwaiter().GetResult();

        static async ValueTask Core(TCollection actual, System.Collections.Generic.IEnumerable<TItem> expected, CollectionOrdering ordering, string? expectedExpression, string? orderingExpression)
        {
            await Assert.That(actual).IsEquivalentTo(expected, ordering, expectedExpression!, orderingExpression!);
        }
    }
}
