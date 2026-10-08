using Newtonsoft.Json.Linq;
using Xunit;

namespace TfPortable.Tests
{
    internal static class JsonAssert
    {
        /// <summary>A JSON null: a null token, or a typed value (string, int, ...) whose value is null; both serialize as `null`.</summary>
        public static void Null(JToken token)
        {
            Assert.True(token.Type == JTokenType.Null || (token is JValue value && value.Value == null), $"expected null, got {token.Type}: {token}");
        }
    }
}
