using System.Runtime.CompilerServices;

namespace SerializationFramework.Tests;

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
    }
}
