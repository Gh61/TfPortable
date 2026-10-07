using TfPortable.Auth;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    public class CollectionUrlTests
    {
        [Theory]
        [InlineData("https://dev.azure.com/contoso", "https://dev.azure.com/contoso")]
        [InlineData("https://dev.azure.com/Contoso/", "https://dev.azure.com/contoso")]
        [InlineData("https://dev.azure.com/contoso/DefaultCollection", "https://dev.azure.com/contoso")]
        [InlineData("https://contoso.visualstudio.com", "https://dev.azure.com/contoso")]
        [InlineData("https://contoso.visualstudio.com/DefaultCollection/", "https://dev.azure.com/contoso")]
        [InlineData("https://tfs.contoso.local/tfs/DefaultCollection", "https://tfs.contoso.local/tfs/defaultcollection")]
        [InlineData("https://tfs.contoso.local:8443/tfs/Other/", "https://tfs.contoso.local:8443/tfs/other")]
        public void Normalizes_services_and_server_urls(string input, string expected)
        {
            Assert.Equal(expected, CollectionUrl.Normalize(input));
        }

        [Fact]
        public void Credential_target_has_the_prefix()
        {
            Assert.Equal("TfPortable:https://dev.azure.com/contoso", CollectionUrl.ToCredentialTarget("https://contoso.visualstudio.com/"));
        }

        [Fact]
        public void Token_page_is_under_the_collection()
        {
            Assert.Equal("https://dev.azure.com/contoso/_usersSettings/tokens", CollectionUrl.TokenPageUrl("https://dev.azure.com/contoso"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("contoso")]
        [InlineData("ftp://contoso")]
        public void Invalid_urls_are_TFP0002(string input)
        {
            var error = Assert.Throws<TfpException>(() => CollectionUrl.Normalize(input));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
        }
    }
}
